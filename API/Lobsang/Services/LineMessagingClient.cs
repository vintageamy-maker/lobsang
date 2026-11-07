using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Lobsang.Data;
using Lobsang.Models;

namespace Lobsang.Services
{
    /// <summary>
    /// 呼叫 LINE Messaging API：回覆（reply）、推播（push），以及共用的卡片。
    ///
    /// ⚠ <b>replyToken 只有約 1 分鐘有效，而且只能用一次。</b>
    ///    這正是 webhook 必須先回 200、把處理丟到背景的原因之一 ——
    ///    但背景也不能拖，拖過期就回不出去了，而且**不會有任何錯誤浮到畫面上**。
    ///
    /// ⚠ Channel Access Token 與 Channel Secret 是**兩把不同的鑰匙**：
    ///    Secret 用來驗進來的簽章，Token 用來發出去的請求。設定時很容易對調。
    ///
    /// ⚠ 這是 Singleton（Program.cs 註冊），所以**不可以直接注入 AppDbContext**；
    ///    要查資料庫一律自己開 scope（見 <see cref="NotifyUserAsync"/>）。
    /// </summary>
    public class LineMessagingClient(
        IHttpClientFactory http, IConfiguration config, IServiceScopeFactory scopeFactory,
        ILogger<LineMessagingClient> logger)
    {
        const string ReplyUrl = "https://api.line.me/v2/bot/message/reply";
        const string PushUrl = "https://api.line.me/v2/bot/message/push";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(config["Line:ChannelAccessToken"]);

        /// <summary>回覆訊息。回傳 null 代表成功，否則是錯誤描述。</summary>
        public Task<string?> ReplyAsync(string replyToken, object[] messages, CancellationToken ct = default) =>
            SendAsync(ReplyUrl, new { replyToken, messages }, "回覆", ct);

        /// <summary>
        /// 主動推播給一個人。回傳 null 代表成功，否則是錯誤描述。
        ///
        /// ⚠⚠ **推播要算錢**（reply 免費、push 依收訊人數計則），所以只給「真的需要的人」：
        ///   呼叫端必須先篩過對象，不要對全體廣播。
        /// ⚠ 一次最多 5 則訊息物件，對同一人只算 1 則。
        /// ⭐ 一般模組請改用 <see cref="NotifyUserAsync"/>（會自己查綁定與在職狀態）。
        /// </summary>
        public Task<string?> PushAsync(string lineUserId, object[] messages, CancellationToken ct = default) =>
            SendAsync(PushUrl, new { to = lineUserId, messages }, "推播", ct);

        async Task<string?> SendAsync(string url, object payload, string what, CancellationToken ct)
        {
            var token = config["Line:ChannelAccessToken"];
            if (string.IsNullOrWhiteSpace(token))
                return $"未設定 Line:ChannelAccessToken，無法{what}";

            var client = http.CreateClient("line");
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            try
            {
                using var res = await client.SendAsync(req, ct);
                if (res.IsSuccessStatusCode) return null;

                // ⚠ LINE 的錯誤訊息在 body 裡，狀態碼本身看不出原因。常見：
                //   400 Invalid reply token ＝ 過期或已用過；400 ＝ 對方封鎖或 userId 錯；429 ＝ 當月則數用完
                var detail = await res.Content.ReadAsStringAsync(ct);
                return $"HTTP {(int)res.StatusCode}：{detail}";
            }
            catch (Exception ex)
            {
                // ⚠ 背景執行緒沒有 HttpContext，用不了 ErrorLogger（那支要 HttpContext），只能寫應用程式日誌
                logger.LogError(ex, "LINE {What}失敗", what);
                return ex.Message;
            }
        }

        /// <summary>
        /// ⭐ 通知某位使用者（以 users.id 指定）。**其他模組要發 LINE 通知一律呼叫這支**，
        ///   例如簽核流程的「有一張單等你簽」「你的單被退回」。
        ///
        /// 回傳 true ＝ 已送出；false ＝ 沒送（未綁定、已停用、未設定 Token、LINE 回錯）。
        ///
        /// ⚠ **永遠不拋例外**：通知是附帶動作，LINE 掛掉不可以讓主流程（送簽、核可）跟著失敗。
        ///   呼叫端不必 try/catch，也**不要**依回傳值決定主流程要不要繼續。
        /// ⚠ 條件含 IsActive —— 有綁定不等於可以用。停用者手機上的 LINE 仍會收得到的話，
        ///   離職後還在收內部通知，那就是資訊外流。
        /// ⚠ 推播要算錢，見 <see cref="PushAsync"/>；呼叫端自己決定誰真的需要收。
        /// </summary>
        public async Task<bool> NotifyUserAsync(int userId, string text, CancellationToken ct = default)
        {
            try
            {
                if (!IsConfigured || string.IsNullOrWhiteSpace(text)) return false;

                string? lineUserId;
                using (var scope = scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    lineUserId = await db.Users
                        .Where(u => u.Id == userId && u.IsActive && u.LineUserId != null)
                        .Select(u => u.LineUserId)
                        .FirstOrDefaultAsync(ct);
                }
                if (string.IsNullOrWhiteSpace(lineUserId)) return false;

                // ⚠ LINE 單則文字上限 5000 字，超過整則會被拒（不是截斷）
                if (text.Length > 4900) text = text[..4900] + "…";

                var err = await PushAsync(lineUserId, [Text(text)], ct);
                if (err is not null)
                    logger.LogWarning("LINE 通知使用者 #{UserId} 失敗：{Error}", userId, err);
                return err is null;
            }
            catch (Exception ex)
            {
                // ⚠ 這裡多半是從請求或背景工作呼叫進來的，拿不到可靠的 HttpContext，故不走 ErrorLogger
                logger.LogError(ex, "LINE 通知使用者 #{UserId} 時發生例外", userId);
                return false;
            }
        }

        /// <summary>單純的文字訊息</summary>
        public static object Text(string text) => new { type = "text", text };

        /// <summary>LIFF 綁定頁的網址。未設定 LiffId 時回 null</summary>
        public string? BindUrl
        {
            get
            {
                var id = config["Line:LiffId"];
                return string.IsNullOrWhiteSpace(id) ? null : $"https://liff.line.me/{id}?action=bind";
            }
        }

        /// <summary>後台網址（Line:AdminUrl）。未設定時 null，卡片就不放那顆按鈕</summary>
        public string? AdminUrl
        {
            get
            {
                var url = config["Line:AdminUrl"];
                return string.IsNullOrWhiteSpace(url) ? null : url.Trim();
            }
        }

        /*
         * ⚠⚠ Flex 的可選欄位**不可以送 null，要整個欄位不出現**。
         *
         * 實測：按鈕沒有指定顏色時輸出了 "color": null，LINE 直接回 400：
         *     {"message":"invalid property","property":"/footer/contents/1/color"}
         * 而且整張卡片都不會出現 —— 從畫面上完全看不出原因。
         * ⇒ 用 Dictionary 動態組欄位：有值才加。
         * ⚠ 不要改用 JsonIgnoreCondition.WhenWritingNull 全域處理 ——
         *   那會影響整個 API 的所有回應，為了一張卡片動全域設定不划算。
         */
        static Dictionary<string, object> UriButton(string label, string uri, string style, string? color = null)
        {
            var btn = new Dictionary<string, object>
            {
                ["type"] = "button",
                ["style"] = style,
                ["height"] = "sm",
                ["action"] = new Dictionary<string, object> { ["type"] = "uri", ["label"] = label, ["uri"] = uri },
            };
            if (!string.IsNullOrWhiteSpace(color)) btn["color"] = color;
            return btn;
        }

        static object Bubble(string altText, string title, string subtitle, List<object> footer, string? titleColor = null)
        {
            var titleText = new Dictionary<string, object>
            {
                ["type"] = "text", ["text"] = title, ["weight"] = "bold", ["size"] = "lg", ["wrap"] = true,
            };
            if (!string.IsNullOrWhiteSpace(titleColor)) titleText["color"] = titleColor;

            var bubble = new Dictionary<string, object>
            {
                ["type"] = "bubble",
                ["body"] = new
                {
                    type = "box", layout = "vertical", spacing = "md",
                    contents = new object[]
                    {
                        titleText,
                        new { type = "text", text = subtitle, size = "sm", color = "#595959", wrap = true },
                    },
                },
            };
            // ⚠ footer 的 contents 不可以是空陣列（LINE 會回 400），沒東西就整個 footer 不出現
            if (footer.Count > 0)
                bubble["footer"] = new { type = "box", layout = "vertical", spacing = "sm", contents = footer.ToArray() };

            return new { type = "flex", altText, contents = bubble };
        }

        /// <summary>
        /// 主選單卡片。
        /// <list type="bullet">
        /// <item>未綁定 → 一顆「綁定帳號」</item>
        /// <item>已綁定 → 顯示綁定的姓名、「開啟後台」（有設 Line:AdminUrl 才有），以及解除綁定的說明與入口</item>
        /// </list>
        /// ⚠ 未設定 LiffId 時退化成文字說明，**不要放一顆點了沒反應的按鈕**。
        /// ⚠⚠ 已綁定的人也要看得到「綁定 / 解除」入口 —— 這張卡是聊天室裡**唯一的自助解綁入口**，
        ///    拿掉的話換手機、綁錯人都只能找管理員。（後台代為解綁是第二條路，供手機遺失時使用，見 AdminUsersController。）
        /// </summary>
        /// <param name="bound">這個 LINE 綁定到的**在職**使用者；null ＝ 未綁定或已停用</param>
        public object MenuCard(User? bound)
        {
            var footer = new List<object>();

            if (bound is null)
            {
                if (BindUrl is null)
                    return Text("主選單\n尚未綁定帳號。⚠ 綁定功能尚未開放，請聯絡管理員。");

                footer.Add(UriButton("綁定帳號", BindUrl, "primary", "#2563eb"));
                return Bubble("主選單", "主選單", "尚未綁定帳號。綁定後系統才知道你是誰，才能收到通知、使用功能。", footer);
            }

            if (AdminUrl is not null) footer.Add(UriButton("開啟後台", AdminUrl, "primary", "#2563eb"));
            if (BindUrl is not null) footer.Add(UriButton("綁定 / 解除", BindUrl, "secondary"));
            footer.Add(new
            {
                type = "text",
                // ⚠ 這句話必須與實際行為一致：沒有 LiffId 時聊天室裡沒有解綁入口，只能找管理員
                text = BindUrl is not null
                    ? "※ 解除綁定：點「綁定 / 解除」即可自助解除；手機遺失請洽管理員代為解除。"
                    : "※ 解除綁定：請洽管理員代為解除。",
                size = "xxs", color = "#8c8c8c", wrap = true,
            });

            return Bubble("主選單", "主選單", $"已綁定：{bound.Name}", footer);
        }

        /// <summary>
        /// 未綁定時的提示。給「需要知道你是誰」的功能在擋下未綁定者時使用。
        ///
        /// ⚠ 這一則**不可以只回「無法使用」** —— 使用者不知道要做什麼，
        ///    只會去問管理員。訊息要同時說明原因**並直接給入口**。
        /// </summary>
        /// <param name="feature">功能名稱；不給就用泛稱。⚠ 不要猜功能名 —— 猜錯會對使用者說出不存在的功能</param>
        public object[] NeedBindCard(string? feature = null)
        {
            var what = string.IsNullOrWhiteSpace(feature) ? "這項功能" : $"「{feature}」";
            var msg = $"要使用{what}需要先綁定帳號，系統才知道你是誰。";
            if (BindUrl is null)
                return [Text(msg + "\n⚠ 綁定功能尚未開放，請聯絡管理員。")];

            return [Bubble("尚未綁定帳號", "尚未綁定帳號", msg,
                [UriButton("立即綁定", BindUrl, "primary", "#2563eb")], titleColor: "#b45309")];
        }
    }
}
