using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    /// <summary>
    /// 後台選單（全域，不分機構）。節點掛 PermissionCode 與權限字典對接：
    /// 角色管理勾選單樹＝授予對應權限；無 Path 者為目錄（僅分組用）。
    /// </summary>
    public class Menu : IAuditable
    {
        public int Id { get; set; }

        public int? ParentId { get; set; }
        public Menu? Parent { get; set; }

        [MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        /// <summary>前端路由路徑（如 /users）；null＝目錄</summary>
        [MaxLength(200)]
        public string? Path { get; set; }

        [MaxLength(50)]
        public string? Icon { get; set; }

        public int SortOrder { get; set; }

        /// <summary>對應的權限代碼（permissions.code）；目錄可為 null</summary>
        [MaxLength(50)]
        public string? PermissionCode { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
    }
}
