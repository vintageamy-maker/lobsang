using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    public class User : IAuditable
    {
        public int Id { get; set; }
        public int InstitutionId { get; set; }
        public Institution? Institution { get; set; }

        [MaxLength(50)]
        public string Account { get; set; } = string.Empty;

        [MaxLength(255)]
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>
        /// 認證密碼（二次密碼）的雜湊。null ＝ 尚未設定，無法進入受 [RequireStepUp] 保護的頁面。
        /// ⚠ 強度規範必須等同或嚴於登入密碼（登入為 8 碼）。
        /// </summary>
        [MaxLength(255)]
        public string? StepUpPasswordHash { get; set; }

        [MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Email { get; set; }

        // ===== 人事欄位 =====

        /// <summary>主歸屬單位（資料可視範圍與簽核鏈的起點）</summary>
        public int? OrgUnitId { get; set; }
        public OrgUnit? OrgUnit { get; set; }

        [MaxLength(20)]
        public string? EmployeeNo { get; set; }

        /// <summary>職稱（純顯示用；程式判斷一律看角色代碼，不看職稱文字）</summary>
        [MaxLength(30)]
        public string? Title { get; set; }

        /// <summary>雇用類別：全職／兼職／約聘</summary>
        [MaxLength(20)]
        public string? EmploymentType { get; set; }

        /// <summary>到職日</summary>
        public DateOnly? HireDate { get; set; }

        /// <summary>離職日</summary>
        public DateOnly? TerminationDate { get; set; }

        /// <summary>離職原因（選填）。只在有離職日時有意義，離職日清空時一併清空</summary>
        [MaxLength(200)]
        public string? TerminationReason { get; set; }

        /// <summary>聯絡電話</summary>
        [MaxLength(50)]
        public string? Phone { get; set; }

        /// <summary>
        /// 綁定的 LINE userId（U 開頭 33 碼）。⚠ 唯一，一個 LINE 不可綁兩個人。
        /// ⚠ 有綁定不等於可以用 —— 每次 LINE 操作仍要查 <see cref="IsActive"/>，
        ///    否則 LINE 會變成繞過離職控管的後門。
        /// </summary>
        [MaxLength(64)]
        public string? LineUserId { get; set; }

        public DateTime? LineBoundAt { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 憑證版本。⚠ 權限放在登入憑證（JWT）裡，拿掉的權限在舊登入到期前仍有效。
        /// 需要立即生效時把該人 +1 ⇒ 手上的舊憑證立刻作廢，要重新登入。
        /// </summary>
        public int TokenVersion { get; set; }
        public bool MustChangePassword { get; set; }
        public DateTime? LastLoginAt { get; set; }

        /// <summary>顯示順序。⚠ 一律以 (SortOrder, Id) 排序，同值時才不會每次順序浮動</summary>
        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }

        public List<UserRole> UserRoles { get; set; } = [];
    }

    public class Role : IAuditable
    {
        public int Id { get; set; }
        public int InstitutionId { get; set; }

        [MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 標準角色的固定代碼（2026-10-09，見 [[96-組織異動影響評估與社保組合併]]），例：director、social_lead。
        ///
        /// ⚠⚠ 為什麼要有：原本程式與種子都用**名稱**認角色 ——
        ///   院方一改名（社工督導 → 社保組長），重啟就再長出一個舊名角色、權限全滿；
        ///   「院長」改名後申訴保密也會悄悄失效。代碼不隨改名變動，程式一律看代碼。
        /// ⚠ 院方在畫面上自建的角色沒有代碼（null）。代碼不可改、不可重複使用。
        /// </summary>
        [MaxLength(40)]
        public string? Code { get; set; }

        [MaxLength(200)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }

        public List<UserRole> UserRoles { get; set; } = [];
        public List<RolePermission> RolePermissions { get; set; } = [];
    }

    public class UserRole
    {
        public int UserId { get; set; }
        public User? User { get; set; }
        public int RoleId { get; set; }
        public Role? Role { get; set; }
    }

    /// <summary>權限代碼（全域字典，不分機構）：API 層以 [RequirePermission("code")] 檢查</summary>
    public class Permission
    {
        public int Id { get; set; }

        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(30)]
        public string? Group { get; set; }

        /// <summary>
        /// 是否可由代理人繼承。薪資等敏感權限設 false：
        /// 即使代理人取得被代理人的其他權限，這類仍不繼承。
        /// </summary>
        public bool IsDelegable { get; set; } = true;
    }

    public class RolePermission
    {
        public int RoleId { get; set; }
        public Role? Role { get; set; }
        public int PermissionId { get; set; }
        public Permission? Permission { get; set; }
    }
}
