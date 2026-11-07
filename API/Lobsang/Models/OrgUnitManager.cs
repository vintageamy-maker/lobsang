using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    /// <summary>
    /// 一個人對一個單位的身分。
    ///
    /// ⚠ 兩種身分刻意放在同一張表：它們是**同一件事的兩種強度**
    /// ——「這個人對這個單位有管理關係」，差別只在要不要進簽核鏈。
    /// 拆成兩張表會讓「督導長也要簽了」這種變更變成資料搬遷。
    /// </summary>
    public enum OrgUnitManagerKind
    {
        /// <summary>
        /// 簽核主管：列入簽核鏈，且**可被申請人指定為該關的簽核人**。
        /// 民生醫院的護理長與副護理長都屬此類（副護理長可能多位）。
        /// </summary>
        Approver,

        /// <summary>
        /// 督導：看得到該單位的資料，但**不進簽核鏈**。
        /// 民生醫院的督導長橫跨數個病房，需要看得到班表與請假，但不簽核。
        /// ⚠ 這也是「跨單位可見」的唯一解 —— 組織樹一個單位只有一個上層，
        /// 督導長管 A/B/C 三個平行病房，用樹是表達不了的。
        /// </summary>
        Viewer,
    }

    /// <summary>
    /// 單位的主管與督導（多對多）。
    ///
    /// ⚠ **與 <see cref="OrgUnit.ManagerUserId"/> 並存，不取代它。**
    /// 判定規則：該單位在此表有 <see cref="OrgUnitManagerKind.Approver"/> 列 → 用這裡的；
    /// 沒有 → 退回 <c>ManagerUserId</c>。
    /// 育幼院 15 人、一單位一主管，沿用舊欄位即可，不必被迫改資料。
    /// </summary>
    public class OrgUnitManager : IAuditable
    {
        public int Id { get; set; }
        public int InstitutionId { get; set; }

        public int OrgUnitId { get; set; }
        public OrgUnit? OrgUnit { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        public OrgUnitManagerKind Kind { get; set; } = OrgUnitManagerKind.Approver;

        /// <summary>
        /// 顯示與預設挑選順序。⚠ 不代表位階 ——
        /// 護理長與副護理長在簽核上是**同一關的候選人**，不是兩關。
        /// </summary>
        public int SortOrder { get; set; }

        /// <summary>職稱備註（「護理長」「副護理長」「督導長」），供畫面顯示</summary>
        [MaxLength(50)]
        public string? TitleNote { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
    }
}
