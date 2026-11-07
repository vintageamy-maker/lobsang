using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    /// <summary>
    /// API 錯誤日誌（與 audit_logs 分工不同）：
    /// audit_logs 記「成功的業務異動」且與業務同交易；
    /// error_logs 記「失敗的請求」，以獨立交易寫入 —— 業務交易回滾時仍留得下來。
    /// </summary>
    public class ErrorLog
    {
        public long Id { get; set; }

        public int? InstitutionId { get; set; }
        public int? UserId { get; set; }

        [MaxLength(50)]
        public string? UserName { get; set; }

        [MaxLength(10)]
        public string Method { get; set; } = string.Empty;

        [MaxLength(300)]
        public string Path { get; set; } = string.Empty;

        public int StatusCode { get; set; }

        /// <summary>業務錯誤代碼（如 duplicate_code / record_locked）</summary>
        [MaxLength(50)]
        public string? ErrorCode { get; set; }

        /// <summary>回應給使用者的訊息</summary>
        [MaxLength(500)]
        public string? Message { get; set; }

        /// <summary>例外類型（僅未處理例外有值）</summary>
        [MaxLength(200)]
        public string? ExceptionType { get; set; }

        /// <summary>堆疊追蹤（僅 5xx 有值）</summary>
        public string? StackTrace { get; set; }

        /// <summary>對應瀏覽器/日誌的追蹤代碼，供跨系統比對</summary>
        [MaxLength(60)]
        public string? TraceId { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
