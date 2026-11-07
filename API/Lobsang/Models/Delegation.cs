using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    /// <summary>
    /// 代理人設定：代理期間內，代理人可代被代理人執行簽核。
    /// 代理操作一律記錄「代誰做」（WorkRecordApprovalStep.OnBehalfOfUserId），
    /// 且不繼承 Permission.IsDelegable = false 的敏感權限（如薪資）。
    /// </summary>
    public class UserDelegation : IAuditable
    {
        public int Id { get; set; }
        public int InstitutionId { get; set; }

        /// <summary>被代理人（權限的原主）</summary>
        public int PrincipalUserId { get; set; }
        public User? Principal { get; set; }

        /// <summary>代理人（期間內取得被代理人的可代理權限）</summary>
        public int DelegateUserId { get; set; }
        public User? Delegate { get; set; }

        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }

        [MaxLength(200)]
        public string? Reason { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
    }
}
