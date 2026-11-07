namespace Lobsang.Models
{
    /// <summary>
    /// 標準角色的固定代碼（存在 roles.code）。
    ///
    /// ⚠⚠ 為什麼要有代碼：程式與種子若用**名稱**認角色 ——
    ///   客戶一改名，重啟就再長出一個舊名角色、權限全滿（SmartCare 2026-09-14 踩過）。
    ///   代碼不隨改名變動，程式一律看代碼。
    /// ⚠ 客戶在畫面上自建的角色沒有代碼（null）。代碼不可改、不可重複使用。
    /// ⭐ 新專案要加標準角色：在這裡加常數，DbSeeder 用 RoleByCodeAsync 建立。
    /// </summary>
    public static class RoleCodes
    {
        public const string SysAdmin = "sysadmin";   // 系統管理員（永遠擁有全部權限）
        public const string Manager = "manager";     // 主管
        public const string Staff = "staff";         // 一般同仁

        /// <summary>
        /// 外部角色（非員工，例如外聘委員）：不會拿到「每個角色都給」的那幾個權限。
        /// ⭐ 新專案有外部角色時把代碼加進來。
        /// </summary>
        public static readonly string[] External = [];
    }

    /// <summary>
    /// 種子「已經做過」的記號（一次性授權、一次性搬移）。
    /// ⚠ 有了它，客戶在畫面上拿掉的權限，重啟才**不會**長回來。
    /// </summary>
    public class SeedMarker
    {
        public int Id { get; set; }
        public int InstitutionId { get; set; }

        [System.ComponentModel.DataAnnotations.MaxLength(120)]
        public string Key { get; set; } = "";

        public DateTime CreatedAt { get; set; }
    }
}
