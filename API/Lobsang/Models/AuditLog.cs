using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    /// <summary>權限/帳號等敏感操作的異動紀錄（改前/改後 JSON）。只寫入不修改。</summary>
    public class AuditLog
    {
        public long Id { get; set; }

        public int InstitutionId { get; set; }

        /// <summary>操作者 user id（系統操作為 null）</summary>
        public int? UserId { get; set; }

        /// <summary>動作代碼，如 user.create / role.update / user.reset_password</summary>
        [MaxLength(50)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(30)]
        public string TargetType { get; set; } = string.Empty;

        public int? TargetId { get; set; }

        public string? BeforeJson { get; set; }
        public string? AfterJson { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
