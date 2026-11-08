using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    /// <summary>
    /// 商品（業主實際販售的東西，2026-11-08）。跟「採購候選」（books，預計要採購的書）分開。
    /// ⭐ 前台只查得到 IsListed＝true 的商品；店內碼（Sku）是業主自訂、同一機構內唯一。
    /// ⭐ 跟 Book 一樣：常用來搜尋的欄位拆成欄位，查書補到的完整資料（封面、作者、定價、簡介…）整包放 DataJson。
    /// 商品名稱一律用業主清單上的名稱（業主決定），不會被查書結果覆蓋。
    /// </summary>
    public class Product : IAuditable
    {
        public int Id { get; set; }
        public int InstitutionId { get; set; }

        /// <summary>自訂店內碼（例 1102260019），同一機構內不重複</summary>
        [MaxLength(30)]
        public string Sku { get; set; } = "";

        /// <summary>商品名稱（業主清單上的）</summary>
        [MaxLength(500)]
        public string Name { get; set; } = "";

        /// <summary>國際條碼（ISBN-13／EAN），可以空白</summary>
        [MaxLength(20)]
        public string Code { get; set; } = "";

        /// <summary>商品中分類，原樣存（例「1 立體書與紙藝」）</summary>
        [MaxLength(100)]
        public string? MidCategory { get; set; }

        /// <summary>商品子分類，原樣存（例「1102 童話與文學故事立體書」）</summary>
        [MaxLength(100)]
        public string? SubCategory { get; set; }

        /// <summary>業主自己的售價（新台幣，整數）。沒設定時前台顯示「價格請洽店員」</summary>
        public int? SalePrice { get; set; }

        /// <summary>庫存數量（前台顯示實際數量，業主決定）</summary>
        public int Stock { get; set; }

        /// <summary>上架：前台只查得到上架的</summary>
        public bool IsListed { get; set; } = true;

        /// <summary>作者／品牌（從查書資料拆出來，前台用作者搜尋時要用）</summary>
        [MaxLength(500)]
        public string? Authors { get; set; }

        /// <summary>查書補到的完整資料（BookInfo 的 JSON），還沒查是 "{}"</summary>
        public string DataJson { get; set; } = "{}";

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
    }
}
