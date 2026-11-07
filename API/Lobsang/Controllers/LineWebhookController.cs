using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using Lobsang.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lobsang.Controllers
{
    /// <summary>
    /// LINE Messaging API 的 webhook 接收端。
    ///
    /// ⚠ **這支必須是匿名的** —— LINE 的伺服器不會帶我們的 JWT。
    ///    身分是靠 <c>X-Line-Signature</c>（用 Channel Secret 對**原始 body** 做 HMAC-SHA256）驗的，
    ///    不是靠登入。
    ///
    /// ⚠ **一定要快速回 200**。LINE 對 webhook 有逾時，回得慢會被判定失敗並重送；
    ///    所以這裡只做「驗章 → 寫一筆 → 回 200」，任何耗時處理都不該放進來。
    ///
    /// ⭐ 事件的實際處理（回什麼）在 <see cref="Services.LineEventProcessor"/>。
    /// </summary>
    [ApiController]
    [Route("api/line")]
    [AllowAnonymous]
    public class LineWebhookController(
        AppDbContext db, IConfiguration config, Services.LineEventQueue queue) : ControllerBase
    {
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            // ⚠⚠ 一定要讀**原始 body 字串**再驗章。
            //    若先反序列化成物件、再序列化回去算 HMAC，欄位順序、空白、跳脫都可能不同，
            //    簽章就永遠對不起來 —— 而且會表現成「LINE 說失敗但我們看起來正常」。
            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            /*
             * ⭐ Channel Secret 支援**逗號分隔的多個候選值**，理由有二：
             *
             * 1. 一個專案下常有兩個 channel（Messaging API 與 LINE Login／LIFF），
             *    兩者各有自己的 secret，而**只有 Messaging API 那個才會用來簽 webhook**。
             *    人在主控台上很容易複製到另一個。全部試一遍就不必猜，
             *    而且下方會記下「是第幾個對的」，一次就能確定該留哪一個。
             * 2. 換 secret 時可以新舊並存，不必挑一個沒有流量的時機切換。
             *
             * ⚠ 這不會降低安全性 —— 每個候選值都是真的 secret，只是多比對幾次。
             */
            var secrets = (config["Line:ChannelSecret"] ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var sigHeader = Request.Headers["X-Line-Signature"].ToString();

            bool verified = false;
            string note;

            if (secrets.Length == 0)
            {
                // ⚠ 沒設定 Channel Secret 時仍然收下，但**明確記成未驗章**。
                //    不這樣做的話，「還沒設定」與「驗過了」在資料上長得一樣，
                //    日後沒有人會發現這個 webhook 其實誰都能打。
                note = "⚠ 未設定 Line:ChannelSecret，未驗簽章（任何人都可送出此請求）";
            }
            else if (string.IsNullOrWhiteSpace(sigHeader))
            {
                /*
                 * ⚠⚠ 2026-08-16 修：這裡原本只記一行訊息就往下走，等於
                 *    **「沒帶簽章」比「簽章錯」還容易通過** —— 偽造者只要不帶那個標頭就行。
                 *
                 * 已設定 secret 卻收到沒有簽章的請求，只有兩種可能：不是 LINE 送的，
                 * 或設定有問題。兩種都不該收下。
                 */
                db.LineWebhookEvents.Add(new LineWebhookEvent
                {
                    ReceivedAt = DateTime.UtcNow, SignatureVerified = false,
                    EventType = "(rejected)", Status = LineEventStatus.Skipped,
                    Note = "⚠ 缺少 X-Line-Signature 標頭，已拒絕",
                    RawJson = Truncate(body, 4000),
                });
                await db.SaveChangesAsync();
                return Unauthorized();
            }
            else
            {
                var matched = -1;
                for (var i = 0; i < secrets.Length && !verified; i++)
                {
                    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secrets[i]));
                    var expected = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
                    // ⚠ 用固定時間比對，不用 == ：字串比較會因提前結束而洩漏長度／內容資訊
                    if (CryptographicOperations.FixedTimeEquals(
                            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(sigHeader)))
                    {
                        verified = true;
                        matched = i;
                    }
                }
                // ⚠ 只記「第幾個」，**不記 secret 本身** —— 資料庫裡不該出現金鑰
                note = verified
                    ? $"簽章驗證通過（第 {matched + 1} 組 secret）"
                    : $"⚠ 簽章不符，已拒絕（試過 {secrets.Length} 組 secret）";

                if (!verified)
                {
                    // ⚠ 狀態要標 Skipped 不能留 Pending —— 被拒絕的請求不會有人去處理，
                    //    留 Pending 會讓「待處理」的數字永遠降不下來
                    db.LineWebhookEvents.Add(new LineWebhookEvent
                    {
                        ReceivedAt = DateTime.UtcNow, SignatureVerified = false,
                        EventType = "(rejected)", Status = LineEventStatus.Skipped,
                        Note = note, RawJson = Truncate(body, 4000),
                    });
                    await db.SaveChangesAsync();
                    return Unauthorized();
                }
            }

            var rows = Parse(body, verified, note);

            /*
             * ⭐ 去重：LINE 沒收到 200 就會**重送同一個事件**，重送時 webhookEventId 不變。
             *    沒有這一段的話，一次網路抖動就會讓同一次操作被記兩遍。
             *
             * ⚠ 先查再寫仍有極小的競態窗（同一事件同時到兩次），
             *    所以資料表上**另有 UNIQUE 索引**當最後一道防線，
             *    這裡的查詢只是為了避免每次都靠例外處理。
             */
            var ids = rows.Select(r => r.WebhookEventId).Where(x => x != null).ToList();
            var known = ids.Count == 0 ? [] : await db.LineWebhookEvents
                .Where(e => ids.Contains(e.WebhookEventId))
                .Select(e => e.WebhookEventId!).ToListAsync();

            var fresh = rows.Where(r => r.WebhookEventId is null || !known.Contains(r.WebhookEventId)).ToList();
            if (fresh.Count > 0)
            {
                db.LineWebhookEvents.AddRange(fresh);
                try { await db.SaveChangesAsync(); }
                catch (DbUpdateException)
                {
                    // UNIQUE 撞上＝別的請求剛剛寫過同一個事件。這是**正常結果不是錯誤**，
                    // 照樣回 200，否則 LINE 會一直重送。
                    return Ok();
                }

                // ⚠ 寫進資料庫**之後**才入佇列 —— 反過來的話，背景可能查不到那一列。
                foreach (var r in fresh) queue.Enqueue(r.Id);
            }

            // ⚠ 一律回 200（驗章失敗的情況上面已經 return 了）。
            //    LINE 只看狀態碼，回別的會被當成失敗並重送。
            return Ok();
        }

        /// <summary>
        /// 把 payload 拆成一筆一筆。
        /// ⚠ LINE 主控台按「Verify」時送的是 <c>{"events":[],"destination":"..."}</c> ——
        ///    events 是**空的**。若因為空陣列就不寫任何一筆，畫面上會看起來像「沒收到」，
        ///    而那正是使用者最需要確認的一刻。故空陣列也寫一筆 (verify)。
        /// </summary>
        static List<LineWebhookEvent> Parse(string body, bool verified, string note)
        {
            var now = DateTime.UtcNow;   // ⚠ 日誌表慣例：存 UTC（見 LineEventQueue 的註解）
            var list = new List<LineWebhookEvent>();
            string? destination = null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("destination", out var d)) destination = d.GetString();

                if (root.TryGetProperty("events", out var events)
                    && events.ValueKind == JsonValueKind.Array
                    && events.GetArrayLength() > 0)
                {
                    foreach (var ev in events.EnumerateArray())
                    {
                        list.Add(new LineWebhookEvent
                        {
                            ReceivedAt = now,
                            SignatureVerified = verified,
                            Destination = destination,
                            // ⭐ 去重的依據。⚠ 舊事件型別可能沒有這個欄位，null 不參與去重
                            WebhookEventId = ev.TryGetProperty("webhookEventId", out var wid)
                                             ? Truncate(wid.GetString(), 64) : null,
                            EventType = ev.TryGetProperty("type", out var t) ? t.GetString() : null,
                            LineUserId = ev.TryGetProperty("source", out var s)
                                         && s.TryGetProperty("userId", out var uid) ? uid.GetString() : null,
                            // ⚠ replyToken 只有約 1 分鐘有效且只能用一次 —— 背景要盡快用掉
                            ReplyToken = ev.TryGetProperty("replyToken", out var rt)
                                         ? Truncate(rt.GetString(), 100) : null,
                            // 圖文選單送的是 postback，data 在這裡（例如 menu=main）
                            PostbackData = ev.TryGetProperty("postback", out var pb)
                                           && pb.TryGetProperty("data", out var pd)
                                           ? Truncate(pd.GetString(), 200) : null,
                            MessageText = ev.TryGetProperty("message", out var m)
                                          && m.TryGetProperty("text", out var txt)
                                          ? Truncate(txt.GetString(), 200) : null,
                            RawJson = Truncate(ev.GetRawText(), 4000),
                            Note = note,
                        });
                    }
                    return list;
                }
            }
            catch (JsonException ex)
            {
                // 收得到但解不開也要留一筆 —— 不然只會表現成「沒收到」
                return [new LineWebhookEvent
                {
                    ReceivedAt = now, SignatureVerified = verified, EventType = "(unparsable)",
                    RawJson = Truncate(body, 4000), Note = $"JSON 解析失敗：{ex.Message}",
                }];
            }

            list.Add(new LineWebhookEvent
            {
                ReceivedAt = now, SignatureVerified = verified, Destination = destination,
                EventType = "(verify)", RawJson = Truncate(body, 4000),
                Note = note + "｜events 為空，應為 LINE 主控台的 Verify 測試",
            });
            return list;
        }

        static string? Truncate(string? s, int max) =>
            s is null ? null : s.Length <= max ? s : s[..max];
    }

    /// <summary>收到的 webhook 事件查詢（給後台確認「真的收到了」）</summary>
    [ApiController]
    [Route("api/admin/line")]
    public class AdminLineWebhookController(AppDbContext db) : ControllerBase
    {
        [HttpGet("webhook-events")]
        [RequirePermission("system.admin")]
        public async Task<IActionResult> Recent(int take = 50)
        {
            var rows = await db.LineWebhookEvents
                .OrderByDescending(e => e.Id).Take(Math.Clamp(take, 1, 200))
                .ToListAsync();
            return Ok(rows);
        }
    }
}
