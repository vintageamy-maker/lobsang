using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace Lobsang.Services.BookLookup
{
    /// <summary>
    /// 用 ISBN／商品條碼查書目或商品資料：同時查好幾個公開資料庫，再合併成一筆。
    /// ⭐ 移植自 C:\Han\book（isbn.html＋isbn-server.ps1，2026-11-07）。原本在瀏覽器查的來源也搬到伺服器，
    ///   這樣 Google 金鑰不會外流、公開頁可以統一限流，國圖也不必再開 PowerShell 小伺服器。
    ///
    /// 來源：
    ///   書　台灣書（978-957／986／626）國圖書號中心 → Google Books → Open Library
    ///   　　日本書（978-4）openBD → Google Books → Open Library
    ///   　　其他　Google Books → Open Library
    ///   商品　Open Food／Beauty／Products／Pet Food Facts、UPCitemdb（免費試用，每天約 100 次）
    /// 合併規則：每個欄位依上面的順序，取第一個有值的來源。
    ///
    /// ⚠ 有查到的結果快取 6 小時、查不到的快取 10 分鐘：同一本書被重複查時不要每次都打外部網站
    ///   （國圖、UPCitemdb 都有被擋或額度的風險）。
    /// </summary>
    public class BookLookupService(IHttpClientFactory httpFactory, IConfiguration config, IMemoryCache cache,
                                   ILogger<BookLookupService> logger)
    {
        public const string HttpClientName = "booklookup";
        const string NclBase = "https://isbn.ncl.edu.tw/NEW_ISBNNet/";
        const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Lobsang-BookLookup";

        public async Task<LookupResult> LookupAsync(string? raw, CancellationToken ct)
        {
            var p = CodeParser.Parse(raw);
            var result = new LookupResult { Kind = p.Kind, Code = p.Code, Notes = p.Notes, Error = p.Error };
            if (p.Error is not null || p.Code is null) return result;

            switch (p.Kind)
            {
                case "book":
                case "product":
                    var cacheKey = $"booklookup:{p.Code}";
                    if (cache.TryGetValue(cacheKey, out LookupResult? cached) && cached is not null)
                        return new LookupResult
                        {
                            Kind = cached.Kind, Code = cached.Code, Notes = p.Notes, Found = cached.Found,
                            Info = cached.Info, Statuses = cached.Statuses, Links = cached.Links,
                        };
                    var r = p.Kind == "book" ? await LookupBookAsync(p.Code, ct) : await LookupProductAsync(p.Code, ct);
                    cache.Set(cacheKey, r, r.Found ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(10));
                    r.Notes = p.Notes;
                    return r;
                default:
                    result.Card = InfoCardOf(p.Kind!, p.Code);
                    return result;
            }
        }

        // ================================================================ 書

        static readonly string[] BookFieldOrder = ["國圖書號中心", "openBD", "Google Books", "Open Library"];

        async Task<LookupResult> LookupBookAsync(string isbn, CancellationToken ct)
        {
            var region = CodeParser.RegionOf(isbn);
            var sources = region switch
            {
                "jp" => new (string Name, Func<Task<BookInfo?>> Fn)[] { ("openBD", () => FromOpenBD(isbn, ct)), ("Google Books", () => FromGoogle(isbn, ct)), ("Open Library", () => FromOpenLibrary(isbn, ct)) },
                "tw" => [("國圖書號中心", () => FromNcl(isbn, ct)), ("Google Books", () => FromGoogle(isbn, ct)), ("Open Library", () => FromOpenLibrary(isbn, ct))],
                _ => [("Google Books", () => FromGoogle(isbn, ct)), ("Open Library", () => FromOpenLibrary(isbn, ct))],
            };
            var (results, statuses) = await RunAllAsync(sources);

            var res = new LookupResult { Kind = "book", Code = isbn, Statuses = statuses, Links = BookLinks(isbn, region) };
            var hits = results.Where(x => x is not null).Cast<BookInfo>().ToList();
            if (hits.Count == 0) return res;

            var o = new BookInfo
            {
                Kind = "book", Code = isbn, Isbn10 = CodeParser.To10(isbn), Region = region,
                RegionLabel = CodeParser.RegionLabel[region],
                Title = First(hits, h => h.Title), Subtitle = First(hits, h => h.Subtitle),
                Authors = FirstList(hits, h => h.Authors), Publisher = First(hits, h => h.Publisher),
                Date = First(hits, h => h.Date), Edition = First(hits, h => h.Edition),
                Pages = hits.Select(h => h.Pages).FirstOrDefault(v => v is > 0),
                Price = First(hits, h => h.Price), Categories = FirstList(hits, h => h.Categories),
                Language = First(hits, h => h.Language), Description = First(hits, h => h.Description),
                Toc = First(hits, h => h.Toc),
                Sources = hits.SelectMany(h => h.Sources).ToList(),
            };
            // 封面：國圖、openBD、Open Library 的圖比較大，優先使用；前端會依序試，壞掉就換下一張
            o.Covers = BookFieldOrder
                .Select(n => hits.FirstOrDefault(h => h.Sources.Contains(n)))
                .Where(h => h is not null).SelectMany(h => h!.Covers).Distinct().ToList();
            res.Found = true;
            res.Info = o;
            return res;
        }

        static List<LinkItem> BookLinks(string isbn, string region)
        {
            var all = new Dictionary<string, LinkItem>
            {
                ["books"] = new("博客來", $"https://search.books.com.tw/search/query/key/{isbn}/cat/all"),
                // ⚠ 誠品線上不能在這裡（伺服器端）查：整站有 Cloudflare 機器人防護，程式查詢一律 403（2026-11-07 確認），業主決定不繞過。
                //    誠品的資料改由前端在使用者瀏覽器查（React/src/eslite.ts），查到時前端會把這個搜尋連結換成商品頁
                ["eslite"] = new("誠品線上", $"https://www.eslite.com/Search?keyword={isbn}"),
                ["ncl"] = new("國圖書號中心", "https://isbn.ncl.edu.tw/NEW_ISBNNet/"),
                ["amzjp"] = new("Amazon.co.jp", $"https://www.amazon.co.jp/s?k={isbn}"),
                ["hanmoto"] = new("版元ドットコム", $"https://www.hanmoto.com/bd/isbn/{isbn}"),
                ["amz"] = new("Amazon.com", $"https://www.amazon.com/s?k={isbn}"),
                ["gb"] = new("Google Books", $"https://books.google.com/books?vid=ISBN{isbn}"),
                ["ol"] = new("Open Library", $"https://openlibrary.org/isbn/{isbn}"),
            };
            string[] order = region switch
            {
                "jp" => ["amzjp", "hanmoto", "books", "eslite", "gb"],
                "tw" => ["books", "eslite", "ncl", "gb"],
                _ => ["amz", "gb", "ol", "books", "eslite"],
            };
            return order.Select(k => all[k]).ToList();
        }

        async Task<BookInfo?> FromOpenBD(string isbn, CancellationToken ct)
        {
            var arr = await GetJsonAsync($"https://api.openbd.jp/v1/get?isbn={isbn}", TimeSpan.FromSeconds(12), ct) as JsonArray;
            var d = arr?.FirstOrDefault();
            if (d is null) return null;
            var s = d["summary"]; var o = d["onix"]; var dd = o?["DescriptiveDetail"];
            var te = dd?["TitleDetail"]?["TitleElement"];
            var names = (dd?["Contributor"] as JsonArray ?? []).Select(c => Str(c?["PersonName"]?["content"])).OfType<string>().ToList();
            var texts = o?["CollateralDetail"]?["TextContent"] as JsonArray ?? [];
            string? Pick(string t) => Str(texts.FirstOrDefault(x => Str(x?["TextType"]) == t)?["Text"]);
            var pr = (o?["ProductSupply"]?["SupplyDetail"]?["Price"] as JsonArray)?.FirstOrDefault();
            var ext = dd?["Extent"] as JsonArray ?? [];
            var pg = ext.FirstOrDefault(e => Str(e?["ExtentType"]) == "11") ?? ext.FirstOrDefault();
            var lang = Str((dd?["Language"] as JsonArray)?.FirstOrDefault()?["LanguageCode"]);
            string? price = null;
            if (decimal.TryParse(Str(pr?["PriceAmount"]), out var amt))
                price = $"{amt.ToString("#,0", CultureInfo.InvariantCulture)} {Str(pr?["CurrencyCode"]) ?? "JPY"}{(Str(pr?["PriceType"]) == "01" ? "（未稅）" : "")}";
            var author = Str(s?["author"]);
            return new BookInfo
            {
                Sources = ["openBD"],
                Title = Str(te?["TitleText"]?["content"]) ?? Str(s?["title"]),
                Subtitle = Str(te?["Subtitle"]?["content"]),
                Authors = names.Count > 0 ? names : author is null ? [] : [author],
                Publisher = Str(s?["publisher"]),
                Date = FmtDate(Str(s?["pubdate"])),
                Pages = int.TryParse(Str(pg?["ExtentValue"]), out var pages) && pages > 0 ? pages : null,
                Price = price,
                Categories = (dd?["Subject"] as JsonArray ?? []).Select(x => Str(x?["SubjectHeadingText"])).OfType<string>().ToList(),
                Language = LangName(lang) ?? "日文",
                Description = Pick("03") ?? Pick("02") ?? Pick("04"),
                Covers = Str(s?["cover"]) is { } cv ? [cv] : [],
            };
        }

        async Task<BookInfo?> FromGoogle(string isbn, CancellationToken ct)
        {
            var key = config["BookLookup:GoogleApiKey"];
            var url = $"https://www.googleapis.com/books/v1/volumes?q=isbn:{isbn}"
                      + (string.IsNullOrWhiteSpace(key) ? "" : "&key=" + Uri.EscapeDataString(key.Trim()));
            var r = await GetJsonAsync(url, TimeSpan.FromSeconds(12), ct);
            var it = (r?["items"] as JsonArray)?.FirstOrDefault();
            if (it is null) return null;
            var v = it["volumeInfo"]; var il = v?["imageLinks"]; var lp = it["saleInfo"]?["listPrice"];
            var cover = Https(Str(il?["thumbnail"]) ?? Str(il?["smallThumbnail"]))?.Replace("&edge=curl", "");
            string? price = null;
            if (lp?["amount"] is JsonValue av && av.TryGetValue<decimal>(out var amount) && amount > 0)
                price = $"{amount.ToString("#,0.##", CultureInfo.InvariantCulture)} {Str(lp["currencyCode"])}";
            return new BookInfo
            {
                Sources = ["Google Books"],
                Title = Str(v?["title"]), Subtitle = Str(v?["subtitle"]),
                Authors = StrList(v?["authors"]), Publisher = Str(v?["publisher"]),
                Date = Str(v?["publishedDate"]),
                Pages = v?["pageCount"] is JsonValue pv && pv.TryGetValue<int>(out var pc) && pc > 0 ? pc : null,
                Price = price, Categories = StrList(v?["categories"]),
                Language = LangName(Str(v?["language"])),
                Description = StripHtml(Str(v?["description"])),
                Covers = cover is null ? [] : [cover],
            };
        }

        async Task<BookInfo?> FromOpenLibrary(string isbn, CancellationToken ct)
        {
            var r = await GetJsonAsync($"https://openlibrary.org/api/books?bibkeys=ISBN:{isbn}&format=json&jscmd=data", TimeSpan.FromSeconds(12), ct);
            var d = r?[$"ISBN:{isbn}"];
            if (d is null) return null;
            var notes = d["notes"] is JsonValue nv ? Str(nv) : Str(d["notes"]?["value"]);
            var pubs = (d["publishers"] as JsonArray ?? []).Select(x => Str(x?["name"])).OfType<string>().ToList();
            var cover = Https(Str(d["cover"]?["large"]) ?? Str(d["cover"]?["medium"]));
            return new BookInfo
            {
                Sources = ["Open Library"],
                Title = Str(d["title"]), Subtitle = Str(d["subtitle"]),
                Authors = (d["authors"] as JsonArray ?? []).Select(a => Str(a?["name"])).OfType<string>().ToList(),
                Publisher = pubs.Count > 0 ? string.Join("、", pubs) : null,
                Date = Str(d["publish_date"]),
                Pages = d["number_of_pages"] is JsonValue np && np.TryGetValue<int>(out var n) && n > 0 ? n : null,
                Categories = (d["subjects"] as JsonArray ?? []).Take(8).Select(x => Str(x?["name"])).OfType<string>().ToList(),
                Description = notes,
                Covers = cover is null ? [] : [cover],
            };
        }

        /// <summary>
        /// 國家圖書館 ISBN 書號中心。沒有 API，只能模擬網頁操作：
        ///   1. 開首頁拿 csrftoken（和 session cookie）
        ///   2. 送出簡易查詢表單
        ///   3. 再開「顯示全部欄位」頁，從 HTML 抓欄位
        /// ⚠ 每次查詢都用自己的 cookie 容器：三個請求要同一個 session，又不能跟別人的查詢混在一起。
        /// ⚠ 國圖改版面時這裡會查不到（回 null 或抓不到欄位），屆時對照網頁原始碼調整 regex。
        /// </summary>
        async Task<BookInfo?> FromNcl(string isbn, CancellationToken ct)
        {
            using var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

            var home = await client.GetStringAsync(NclBase, ct);
            var tok = Regex.Match(home, "name=\"csrftoken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            var form = new List<KeyValuePair<string, string>>
            {
                new("FO_SchRe1ation0", "Null"), new("FO_SearchField0", "ISBN"), new("FO_SearchValue0", isbn),
                new("FB_clicked", "FB_開始查詢"), new("FB_pageSID", "Simple"), new("FO_Match", "2"),
                new("FO_每頁筆數", "10"), new("FO_目前頁數", "1"), new("FO_資料排序", "PubMonth_Pre DESC"),
                new("FB_ListOri", ""), new("csrftoken", tok),
            };
            using var resp = await client.PostAsync(NclBase + "H30_SearchBooks.php?Pact=DisplayAll4Simple",
                new FormUrlEncodedContent(form), ct);
            var res = await resp.Content.ReadAsStringAsync(ct);
            var n = Regex.Match(res, @"找到\s*(?:<[^>]+>\s*)*(\d+)").Groups[1].Value;
            if (!int.TryParse(n, out var count) || count == 0) return null;

            var d = await client.GetStringAsync(NclBase + "main_DisplayRecord.php?&Pact=init&Pstart=1", ct);
            var isbnCell = NclCell(d, "ISBN(裝訂方式)");
            var binding = Regex.Match(isbnCell, @"\(([^)]+)\)") is { Success: true } bm ? bm.Groups[1].Value : null;
            var edition = string.Join("・", new[] { NclField(d, "出版版次"), binding }.Where(s => !string.IsNullOrEmpty(s)));
            var keywords = Regex.Split(NclField(d, "關鍵字詞"), "[;；]").Select(s => s.Trim()).Where(s => s.Length > 0);
            var author = NclField(d, "作者");
            return new BookInfo
            {
                Sources = ["國圖書號中心"],
                Title = NullIfEmpty(NclField(d, "書名")),
                Authors = author.Length > 0 ? [author] : [],
                Publisher = NullIfEmpty(NclField(d, "出版機構")),
                Date = NullIfEmpty(RocDate(NclCell(d, "出版年月"))),
                Edition = NullIfEmpty(edition),
                Pages = int.TryParse(NclCell(d, "頁數"), out var pg) && pg > 0 ? pg : null,
                Price = NullIfEmpty(NclCell(d, "定價")),
                Categories = new[] { NclField(d, "建議上架分類") }.Concat(keywords).Where(s => s.Length > 0).ToList(),
                Language = "中文",
                Description = NullIfEmpty(NclBlock(d, "簡介")),
                Toc = NullIfEmpty(NclBlock(d, "目次")),
                Covers = [$"{NclBase}main_DisplayResults.php?Pact=ShowFronPage&Pisbn={isbn}"],
            };
        }

        // 上方書目表：<td aria-label="書名">書名</td><td>…</td>
        static string NclField(string html, string label) =>
            Regex.Match(html, "<td aria-label=\"" + Regex.Escape(label) + "\">[^<]*</td>\\s*<td>(.*?)</td>", RegexOptions.Singleline)
                is { Success: true } m ? CleanHtml(m.Groups[1].Value) : "";
        // 出版資訊表：<td data-th="頁數">248</td>
        static string NclCell(string html, string label) =>
            Regex.Match(html, "data-th=\"" + Regex.Escape(label) + "\"\\s*>(.*?)</td>", RegexOptions.Singleline)
                is { Success: true } m ? CleanHtml(m.Groups[1].Value) : "";
        // 簡介、目次
        static string NclBlock(string html, string label) =>
            Regex.Match(html, "<div class='col-sm-2'>" + Regex.Escape(label) + "</div><div class='col-sm-10'>(.*?)</div></div>", RegexOptions.Singleline)
                is { Success: true } m ? CleanHtml(m.Groups[1].Value) : "";
        // 民國 115/05 → 2026-05
        static string RocDate(string s) =>
            Regex.Match(s, @"^(\d{2,3})/(\d{1,2})$") is { Success: true } m
                ? $"{int.Parse(m.Groups[1].Value) + 1911}-{int.Parse(m.Groups[2].Value):00}" : s;

        // ================================================================ 商品

        const string OffFields = "product_name,product_name_zh,product_name_ja,product_name_en,generic_name,brands,quantity,categories,countries,ingredients_text,ingredients_text_zh,image_front_url,image_url";

        async Task<LookupResult> LookupProductAsync(string code, CancellationToken ct)
        {
            var sources = new (string Name, Func<Task<BookInfo?>> Fn)[]
            {
                ("Open Food Facts", () => FromOff("openfoodfacts", "Open Food Facts", code, ct)),
                ("Open Beauty Facts", () => FromOff("openbeautyfacts", "Open Beauty Facts", code, ct)),
                ("Open Products Facts", () => FromOff("openproductsfacts", "Open Products Facts", code, ct)),
                ("Open Pet Food Facts", () => FromOff("openpetfoodfacts", "Open Pet Food Facts", code, ct)),
                ("UPCitemdb", () => FromUpc(code, ct)),
            };
            var (results, statuses) = await RunAllAsync(sources);
            var gs1 = CodeParser.Gs1Country(code);
            var res = new LookupResult
            {
                Kind = "product", Code = code, Statuses = statuses,
                Links =
                [
                    new("Google", $"https://www.google.com/search?q={code}"),
                    new("UPCitemdb", $"https://www.upcitemdb.com/upc/{code}"),
                    new("Open Food Facts", $"https://world.openfoodfacts.org/product/{code}"),
                ],
            };
            var hits = results.Where(x => x is not null).Cast<BookInfo>().ToList();
            if (hits.Count == 0)
            {
                // 查不到時仍告訴使用者條碼註冊地
                if (gs1 is not null) res.Info = new BookInfo { Kind = "product", Code = code, Gs1 = gs1, Region = "product" };
                return res;
            }
            res.Found = true;
            res.Info = new BookInfo
            {
                Kind = "product", Code = code, Region = "product", Gs1 = gs1,
                Title = First(hits, h => h.Title), Brand = First(hits, h => h.Brand),
                Quantity = First(hits, h => h.Quantity), Categories = FirstList(hits, h => h.Categories),
                Countries = First(hits, h => h.Countries), Ingredients = First(hits, h => h.Ingredients),
                Description = First(hits, h => h.Description),
                Covers = hits.SelectMany(h => h.Covers).Distinct().ToList(),
                Sources = hits.SelectMany(h => h.Sources).ToList(),
            };
            return res;
        }

        async Task<BookInfo?> FromOff(string site, string label, string code, CancellationToken ct)
        {
            JsonNode? d;
            try { d = await GetJsonAsync($"https://world.{site}.org/api/v2/product/{code}.json?fields={OffFields}", TimeSpan.FromSeconds(10), ct); }
            catch (LookupHttpException e) when (e.Status == 404) { return null; }
            if (d is null || Str(d["status"]) != "1" || d["product"] is not JsonNode p) return null;
            var title = Str(p["product_name_zh"]) ?? Str(p["product_name"]) ?? Str(p["product_name_ja"])
                        ?? Str(p["product_name_en"]) ?? Str(p["generic_name"]);
            var brand = Str(p["brands"]);
            if (title is null && brand is null) return null;
            var generic = Str(p["generic_name"]);
            return new BookInfo
            {
                Kind = "product", Sources = [label], Title = title, Brand = brand, Quantity = Str(p["quantity"]),
                Categories = (Str(p["categories"]) ?? "").Split(',').Select(s => s.Trim())
                    .Where(s => s.Length > 0 && !Regex.IsMatch(s, @"^\w\w:")).Take(8).ToList(),
                Countries = Str(p["countries"]),
                Ingredients = Str(p["ingredients_text_zh"]) ?? Str(p["ingredients_text"]),
                Description = generic is not null && generic != title ? generic : null,
                Covers = new[] { Str(p["image_front_url"]), Str(p["image_url"]) }.OfType<string>().ToList(),
            };
        }

        async Task<BookInfo?> FromUpc(string code, CancellationToken ct)
        {
            var d = await GetJsonAsync($"https://api.upcitemdb.com/prod/trial/lookup?upc={code}", TimeSpan.FromSeconds(20), ct);
            var it = (d?["items"] as JsonArray)?.FirstOrDefault();
            if (it is null) return null;
            var desc = Str(it["description"]);
            return new BookInfo
            {
                Kind = "product", Sources = ["UPCitemdb"], Title = Str(it["title"]), Brand = Str(it["brand"]),
                Quantity = NullIfEmpty(string.Join("・", new[] { Str(it["size"]), Str(it["weight"]) }.OfType<string>())),
                Categories = (Str(it["category"]) ?? "").Split('>').Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
                Description = desc is not null && !Regex.IsMatch(desc, @"^\s*ingredients", RegexOptions.IgnoreCase) ? desc : null,
                Covers = StrList(it["images"]).Select(Https).OfType<string>().ToList(),
            };
        }

        // ================================================================ 不能查的條碼

        static readonly string[] JpTarget = ["一般", "教養", "實用", "專門", "檢定教科書・其他", "婦女", "學習參考書 I（國中小）", "學習參考書 II（高中）", "兒童", "雜誌扱い"];
        static readonly string[] JpForm = ["單行本", "文庫", "新書", "全集・叢書", "MOOK・其他", "辭典・事典", "圖鑑", "繪本", "光碟等媒體", "漫畫"];

        static InfoCard InfoCardOf(string kind, string c)
        {
            List<string[]> Rows(params (string K, string? V)[] rows) =>
                rows.Where(r => !string.IsNullOrEmpty(r.V)).Select(r => new[] { r.K, r.V! }).ToList();
            switch (kind)
            {
                case "issn":
                    var issn = CodeParser.IssnOf(c);
                    return new InfoCard("這是期刊／雜誌條碼（ISSN）", "目前沒有免費、可自動查詢的 ISSN 資料庫，請用下面的連結查。",
                        Rows(("ISSN", issn), ("期號／版本碼", c[10..12]), ("條碼", c)),
                        [
                            new("ISSN Portal", $"https://portal.issn.org/resource/ISSN/{issn}"),
                            new("Google", $"https://www.google.com/search?q={Uri.EscapeDataString("ISSN " + issn)}"),
                            new("博客來", $"https://search.books.com.tw/search/query/key/{c}/cat/all"),
                        ]);
                case "jp2":
                    var cc = c[3..7]; var price = int.Parse(c[7..12]);
                    return new InfoCard("這是日本書背面「下面那條」條碼",
                        "日本書背面有兩條條碼：上面 978 開頭的是 ISBN，下面 192 開頭的記錄分類和定價。請改掃上面那條才查得到書。",
                        Rows(("分類碼", "C" + cc), ("販售對象", JpTarget[cc[0] - '0']), ("發行形態", JpForm[cc[1] - '0']),
                             ("內容代碼", cc[2..]), ("定價", price > 0 ? $"¥{price:#,0}（未稅）" : null), ("條碼", c)), []);
                case "store":
                    return new InfoCard("無法查詢的條碼", "這是店內碼（2 開頭），只在特定商店或秤重商品使用，沒有公開資料可以查。", Rows(("條碼", c)), []);
                default:
                    return new InfoCard("無法查詢的條碼", "這是優惠券或退款條碼，沒有商品資料可以查。", Rows(("條碼", c)), []);
            }
        }

        // ================================================================ 共用

        /// <summary>所有來源同時查；個別來源失敗不影響其他來源，失敗原因寫進狀態</summary>
        async Task<(BookInfo?[] Results, List<SourceStatus> Statuses)> RunAllAsync((string Name, Func<Task<BookInfo?>> Fn)[] sources)
        {
            var tasks = sources.Select(async s =>
            {
                try
                {
                    var v = await s.Fn();
                    return (v, new SourceStatus(s.Name, v is null ? "none" : "ok"));
                }
                catch (Exception e)
                {
                    var note = e switch
                    {
                        LookupHttpException { Status: 429 } => "額度用完",
                        LookupHttpException { Status: 400 or 403 } => "金鑰無效或被拒",
                        LookupHttpException h => $"HTTP {h.Status}",
                        TaskCanceledException or TimeoutException => "逾時",
                        _ => "連線失敗",
                    };
                    logger.LogWarning(e, "查書來源 {Source} 失敗：{Note}", s.Name, note);
                    return ((BookInfo?)null, new SourceStatus(s.Name, "err", note));
                }
            }).ToArray();
            var done = await Task.WhenAll(tasks);
            return (done.Select(d => d.Item1).ToArray(), done.Select(d => d.Item2).ToList());
        }

        async Task<JsonNode?> GetJsonAsync(string url, TimeSpan timeout, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var client = httpFactory.CreateClient(HttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd(UserAgent);
            using var resp = await client.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode) throw new LookupHttpException((int)resp.StatusCode);
            var text = await resp.Content.ReadAsStringAsync(cts.Token);
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }

        static string? First(List<BookInfo> hits, Func<BookInfo, string?> f) =>
            hits.Select(f).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        static List<string> FirstList(List<BookInfo> hits, Func<BookInfo, List<string>> f) =>
            hits.Select(f).FirstOrDefault(v => v.Count > 0) ?? [];

        static string? Str(JsonNode? n)
        {
            if (n is not JsonValue v) return null;
            var s = v.TryGetValue<string>(out var str) ? str : v.ToJsonString().Trim('"');
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
        static List<string> StrList(JsonNode? n) => (n as JsonArray ?? []).Select(Str).OfType<string>().ToList();
        static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
        static string? Https(string? u) => u is null ? null : Regex.Replace(u, "^http:", "https:");

        static string? FmtDate(string? s)
        {
            if (s is null) return null;
            s = s.Trim();
            if (Regex.IsMatch(s, @"^\d{8}$")) return $"{s[..4]}-{s[4..6]}-{s[6..]}";
            if (Regex.IsMatch(s, @"^\d{6}$")) return $"{s[..4]}-{s[4..]}";
            return s;
        }

        static readonly Dictionary<string, string> Lang = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ja"] = "日文", ["jpn"] = "日文", ["zh"] = "中文", ["zh-TW"] = "中文", ["zh-CN"] = "中文", ["chi"] = "中文", ["zho"] = "中文",
            ["en"] = "英文", ["eng"] = "英文", ["fr"] = "法文", ["fre"] = "法文", ["fra"] = "法文", ["de"] = "德文", ["ger"] = "德文",
            ["deu"] = "德文", ["es"] = "西班牙文", ["spa"] = "西班牙文", ["it"] = "義大利文", ["ita"] = "義大利文", ["ko"] = "韓文", ["kor"] = "韓文",
        };
        static string? LangName(string? c) => c is null ? null : Lang.GetValueOrDefault(c, c);

        static string CleanHtml(string h)
        {
            if (string.IsNullOrEmpty(h)) return "";
            var t = Regex.Replace(h, @"[\r\n]+", "");
            t = Regex.Replace(t, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "<[^>]+>", "");
            t = WebUtility.HtmlDecode(t);
            t = string.Join("\n", t.Split('\n').Select(x => x.Trim()));
            return Regex.Replace(t, @"\n{3,}", "\n\n").Trim();
        }

        static string? StripHtml(string? s)
        {
            if (s is null) return null;
            var t = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "</p>", "\n\n", RegexOptions.IgnoreCase);
            t = WebUtility.HtmlDecode(Regex.Replace(t, "<[^>]+>", ""));
            return NullIfEmpty(Regex.Replace(t, @"\n{3,}", "\n\n").Trim());
        }
    }

    public class LookupHttpException(int status) : Exception($"HTTP {status}")
    {
        public int Status { get; } = status;
    }
}
