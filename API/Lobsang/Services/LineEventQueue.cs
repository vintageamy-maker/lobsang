using Microsoft.EntityFrameworkCore;
using Lobsang.Data;
using Lobsang.Models;
using System.Threading.Channels;

namespace Lobsang.Services
{
    /// <summary>
    /// 待處理的 LINE 事件佇列（行程內）。
    ///
    /// ⚠ <b>行程內佇列在 API 重啟時會清空。</b> 這是刻意接受的代價 ——
    ///    事件本體**在入佇列前已經寫進 <c>line_webhook_events</c>**，
    ///    狀態停在 <see cref="LineEventStatus.Pending"/>，補得回來。
    ///    真正會掉的只有「這一次的即時回覆」，而 replyToken 本來也只有一分鐘壽命，
    ///    重啟後就算補送也已經過期 —— 換成外部佇列也救不回來。
    ///
    /// ⚠ 容量刻意設上限並在滿時**丟棄最舊的**：LINE 遇到我們沒回 200 會重送，
    ///    無上限的佇列在異常時會把記憶體吃光，那比漏一則通知嚴重得多。
    /// </summary>
    public class LineEventQueue
    {
        readonly Channel<int> _channel = Channel.CreateBounded<int>(
            new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });

        /// <summary>把資料列 id 丟進佇列。⚠ 不丟事件本體 —— 內容已經在資料庫，傳 id 就好</summary>
        public bool Enqueue(int eventId) => _channel.Writer.TryWrite(eventId);

        public IAsyncEnumerable<int> ReadAllAsync(CancellationToken ct) =>
            _channel.Reader.ReadAllAsync(ct);
    }

    /// <summary>
    /// 背景處理 LINE 事件：從佇列取出 → 依事件內容決定要回什麼 → 呼叫 LINE reply API。
    ///
    /// ⚠ 這裡**不可以拋出例外**。BackgroundService 的未處理例外會讓整個 Host 停掉，
    ///    也就是一則處理不了的 LINE 訊息會把整個後端弄掛。故逐則 try/catch。
    /// </summary>
    public class LineEventProcessor(
        LineEventQueue queue,
        IServiceScopeFactory scopeFactory,
        LineMessagingClient line,
        ILogger<LineEventProcessor> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var id in queue.ReadAllAsync(stoppingToken))
            {
                try { await HandleAsync(id, stoppingToken); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { logger.LogError(ex, "處理 LINE 事件 #{Id} 失敗", id); }
            }
        }

        async Task HandleAsync(int id, CancellationToken ct)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var ev = await db.LineWebhookEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (ev is null) return;

            /*
             * ⭐ 查這個 LINE 綁到誰。⚠ 條件含 IsActive ——
             *    「綁過」不等於「現在可以用」。停用（離職）後仍能用 LINE 操作，
             *    就是一個繞過帳號停用控管的後門。「啟用是唯一判準」。
             *    停用者在這裡會被當成「未綁定」，看到的是綁定提示而不是功能。
             */
            var bound = string.IsNullOrWhiteSpace(ev.LineUserId) ? null
                : await db.Users.FirstOrDefaultAsync(
                    u => u.LineUserId == ev.LineUserId && u.IsActive, ct);

            var (messages, note) = Decide(ev, line, bound);

            if (messages is null)
            {
                ev.Status = LineEventStatus.Skipped;
                ev.Note = Append(ev.Note, note);
            }
            else if (string.IsNullOrWhiteSpace(ev.ReplyToken))
            {
                // 沒有 replyToken 就回不了 —— 記下來而不是靜靜跳過
                ev.Status = LineEventStatus.Skipped;
                ev.Note = Append(ev.Note, "無 replyToken，無法回覆");
            }
            else
            {
                var err = await line.ReplyAsync(ev.ReplyToken, messages, ct);
                ev.Status = err is null ? LineEventStatus.Done : LineEventStatus.Failed;
                ev.Note = Append(ev.Note, err is null ? $"已回覆：{note}" : $"回覆失敗：{err}");
            }

            /*
             * ⚠⚠ 這裡是 **UtcNow，不是 UtcNow.AddHours(8)**。
             *
             * `line_webhook_events` 是日誌表，與稽核紀錄、錯誤日誌同一種慣例：**存 UTC**。
             * 原本寫 `DateTime.Now`，在容器裡結果是對的（容器時區就是 UTC），
             * 但那是**碰巧** —— 只要有人把容器時區設成 Asia/Taipei 就會無聲壞掉。
             * 改成明寫 UtcNow：行為完全不變，但不再依賴容器時區。
             *
             * ⚠ 日誌／稽核／綁定時間一律存 UTC，前端加 'Z' 再換算。
             *   若日後有業務欄位刻意存當地時間（前端原樣顯示），看到 DateTime.Now
             *   **不可以直接判定是錯的**，要先看那個欄位在畫面上怎麼顯示。
             */
            ev.ProcessedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        /// <summary>
        /// 這個事件要回什麼。回傳 null 代表不需要回覆。
        ///
        /// ⭐ 範本只做「綁定 ＋ 主選單」。新專案要加聊天室功能時：
        ///   在下面的 switch 加分支；需要知道「你是誰」的功能，未綁定時回
        ///   <see cref="LineMessagingClient.NeedBindCard"/>，**不可沉默也不可假裝可用**。
        /// ⚠ 圖文選單（rich menu）的 postback data 定義在 LINE 主控台／選單設定檔，
        ///    **那裡是主檔**。改那邊就要改這裡，兩邊對不上的症狀是「點了沒反應」。
        /// </summary>
        /// <param name="bound">這個 LINE 綁定到的**在職**使用者；null ＝ 未綁定或已停用</param>
        static (object[]? Messages, string Note) Decide(
            LineWebhookEvent ev, LineMessagingClient line, User? bound)
        {
            var who = bound is null ? "（未綁定）" : $"（{bound.Name}）";

            switch (ev.EventType)
            {
                // 加好友第一件事就是引導綁定 —— 主選單在未綁定時就是一顆「綁定帳號」
                case "follow":
                    return ([
                        LineMessagingClient.Text(bound is null
                            ? "歡迎加入！請先綁定帳號，系統才知道你是誰。"
                            : $"歡迎回來，{bound.Name}。"),
                        line.MenuCard(bound),
                    ], "加好友" + who);

                case "message":
                {
                    /*
                     * ⭐ 文字訊息一律回主選單 —— 使用者不知道要打什麼關鍵字，
                     *    任何一句話都能把入口叫出來，比「無法辨識」有用得多。
                     * ⚠ 「綁定」關鍵字：未綁定時直接給綁定卡（說明 ＋ 按鈕），比主選單更直接。
                     */
                    var text = ev.MessageText?.Trim() ?? "";
                    if (bound is null && text.Contains("綁定"))
                        return (line.NeedBindCard(), "關鍵字「綁定」" + who);
                    return ([line.MenuCard(bound)], "主選單" + who);
                }

                case "postback":
                    /*
                     * ⚠ 圖文選單的按鈕（menu=…）一律回主選單：選單按鈕「點了沒反應」
                     *    比回一張主選單更糟，因為使用者看不出哪裡壞了。
                     * ⚠ 其他 postback（例如已移除功能的舊卡片按鈕）不回覆，只記下來 ——
                     *    舊卡片還留在聊天紀錄裡，被點到是正常的，不是錯誤。
                     */
                    if (ev.PostbackData?.StartsWith("menu=", StringComparison.Ordinal) == true)
                        return ([line.MenuCard(bound)], $"主選單（{ev.PostbackData}）" + who);
                    return (null, $"無對應處理的 postback（data={ev.PostbackData}），已略過");

                default:
                    return (null, $"無對應處理（type={ev.EventType}）");
            }
        }

        static string Append(string? origin, string add) =>
            string.IsNullOrWhiteSpace(origin) ? add : $"{origin}｜{add}";
    }
}
