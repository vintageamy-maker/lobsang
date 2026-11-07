using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    /// <summary>
    /// 組織編制單位（機構 → 部門 → 組 的樹狀結構）。
    /// 這是簽核鏈與資料可視範圍的單一依據：單據依此逐級往上找主管，
    /// 主管的「本單位」查詢範圍亦由此推導。
    /// </summary>
    public class OrgUnit : IAuditable
    {
        public int Id { get; set; }
        public int InstitutionId { get; set; }

        public int? ParentId { get; set; }
        public OrgUnit? Parent { get; set; }

        /// <summary>單位代碼（如 HQ、ADM、IT）</summary>
        [MaxLength(20)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        /// <summary>層級類型（機構／部門／組…，純顯示與篩選用）</summary>
        [MaxLength(20)]
        public string Kind { get; set; } = "部門";

        /// <summary>單位主管（簽核鏈的下一關）；未設定時往上一層找</summary>
        public int? ManagerUserId { get; set; }

        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
    }
}
