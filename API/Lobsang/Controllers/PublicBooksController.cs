using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Lobsang.Data;
using Lobsang.Services.BookLookup;

namespace Lobsang.Controllers
{
    /// <summary>
    /// 公開頁（主站，不用登入）用的查書 API：
    ///   GET /api/public/books/lookup?code=  用 ISBN／條碼查任何一本書或商品（查外部資料庫）
    ///   GET /api/public/books?q=            搜尋業主建立的書單
    /// ⚠ 不用登入 ⇒ 一律掛限流（以 IP 計，見 Program.cs 的 book-lookup／book-search）。
    ///   查外部資料庫的那支特別嚴：國圖、UPCitemdb 被大量查詢時可能把我們的伺服器擋掉。
    /// ⚠ 業主的備註（Note）不給公開頁看。
    /// </summary>
    [ApiController]
    [Route("api/public/books")]
    public class PublicBooksController(AppDbContext db, BookLookupService lookup) : ControllerBase
    {
        internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        [HttpGet("lookup")]
        [EnableRateLimiting(BookRateLimits.Lookup)]
        public async Task<IActionResult> Lookup([FromQuery] string? code, CancellationToken ct)
        {
            var r = await lookup.LookupAsync(code, ct);
            // 順便告訴使用者「這本在不在書單裡」
            var inList = r.Code is not null && await db.Books.AnyAsync(b => b.Code == r.Code, ct);
            return Ok(new { result = r, inList });
        }

        [HttpGet]
        [EnableRateLimiting(BookRateLimits.Search)]
        public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int page = 1, CancellationToken ct = default)
        {
            const int pageSize = 20;
            page = Math.Max(1, page);
            var query = BookQueries.Filter(db.Books.AsNoTracking(), q);
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return Ok(new
            {
                total, page, pageSize,
                items = rows.Select(b => new
                {
                    code = b.Code, kind = b.Kind, title = b.Title, authors = b.Authors,
                    publisher = b.Publisher, pubDate = b.PubDate,
                    info = BookQueries.ParseInfo(b.DataJson),
                }),
            });
        }
    }

    public static class BookRateLimits
    {
        public const string Lookup = "book-lookup";
        public const string Search = "book-search";
    }

    internal static class BookQueries
    {
        /// <summary>關鍵字比對：條碼、書名、作者／品牌、出版社（空白分隔的每個字都要對到）</summary>
        public static IQueryable<Models.Book> Filter(IQueryable<Models.Book> query, string? q)
        {
            if (string.IsNullOrWhiteSpace(q)) return query;
            foreach (var raw in q.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5))
            {
                var w = raw.Replace("-", "");
                query = query.Where(b => b.Code.Contains(w) || b.Title.Contains(raw)
                                         || (b.Authors != null && b.Authors.Contains(raw))
                                         || (b.Publisher != null && b.Publisher.Contains(raw)));
            }
            return query;
        }

        public static BookInfo? ParseInfo(string json)
        {
            try { return JsonSerializer.Deserialize<BookInfo>(json, PublicBooksController.Json); }
            catch (JsonException) { return null; }
        }
    }
}
