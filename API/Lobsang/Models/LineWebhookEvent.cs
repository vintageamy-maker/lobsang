using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    /// <summary>事件處理狀態</summary>
    public enum LineEventStatus
    {
        /// <summary>已收下，還沒處理</summary>
        Pending,
        /// <summary>處理完成</summary>
        Done,
        /// <summary>處理失敗，原因見 Note</summary>
        Failed,
        /// <summary>不需要處理（驗證請求、無對應處理器）</summary>
        Skipped,
    }

    /// <summary>
    /// 收到的 LINE webhook 事件。
    ///
    /// ⭐ 以 <see cref="WebhookEventId"/> 去重（UNIQUE）。
    ///    LINE 在沒收到 200 時會**重送同一個事件**，而重送時 webhookEventId 不變。
    ///    沒有這個唯一鍵的話，一次網路抖動就會讓同一次操作被處理兩遍。
    ///
    /// ⚠ 但 webhookEventId **不保證一定有**（很舊的事件型別、或我們自己補寫的列）。
    ///    為 null 時不參與去重 —— MySQL 的 UNIQUE 允許多個 NULL，正好是我們要的行為。
    /// </summary>
    public class LineWebhookEvent
    {
        public int Id { get; set; }

        /// <summary>
        /// LINE 給的事件唯一識別。⭐ 去重就靠它。
        /// ⚠ 可為 null（驗證請求沒有這個欄位），null 不參與去重。
        /// </summary>
        [MaxLength(64)]
        public string? WebhookEventId { get; set; }

        /// <summary>伺服器收到的時刻</summary>
        public DateTime ReceivedAt { get; set; }

        public LineEventStatus Status { get; set; } = LineEventStatus.Pending;

        /// <summary>背景處理完成的時刻</summary>
        public DateTime? ProcessedAt { get; set; }

        /// <summary>回覆用的權杖。⚠ **只有約 1 分鐘有效**，且只能用一次</summary>
        [MaxLength(100)]
        public string? ReplyToken { get; set; }

        /// <summary>postback 的 data，例如 <c>menu=main</c></summary>
        [MaxLength(200)]
        public string? PostbackData { get; set; }

        /// <summary>
        /// 簽章是否驗過。
        ///
        /// ⚠ **false 不代表一定是偽造的** —— 也可能是還沒設定 Channel Secret。
        ///    兩者的差別看 <see cref="Note"/>。做成兩個欄位而不是一個布林，
        ///    是為了不讓「還沒設定」與「驗過了」在畫面上長得一樣。
        /// </summary>
        public bool SignatureVerified { get; set; }

        /// <summary>LINE 的 destination（Bot 的 userId）</summary>
        [MaxLength(64)]
        public string? Destination { get; set; }

        /// <summary>message / follow / unfollow / postback…；驗證請求為 (verify)</summary>
        [MaxLength(32)]
        public string? EventType { get; set; }

        /// <summary>事件來源的 LINE userId（U 開頭 33 碼）。綁定流程要用</summary>
        [MaxLength(64)]
        public string? LineUserId { get; set; }

        /// <summary>訊息事件的文字內容，方便在畫面上一眼看出「真的收到了」</summary>
        [MaxLength(200)]
        public string? MessageText { get; set; }

        /// <summary>原始 JSON（單一事件）。⚠ 過長會截斷，這裡只為了排查不為了重放</summary>
        [MaxLength(4000)]
        public string? RawJson { get; set; }

        [MaxLength(200)]
        public string? Note { get; set; }
    }
}
