using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    /// <summary>
    /// 書單（業主自己建立的書籍／商品清單）。公開頁可以搜尋，只有 book.manage 能新增、修改、刪除。
    /// ⭐ 常用來搜尋、排序的欄位拆成欄位；查詢當下合併好的完整資料（簡介、目次、封面…）整包存在 DataJson，
    ///   之後來源多了欄位也不必改資料表。
    /// </summary>
    public class Book : IAuditable
    {
        public int Id { get; set; }
        public int InstitutionId { get; set; }

        /// <summary>ISBN-13 或商品條碼（EAN-13／EAN-8），同一機構內不重複</summary>
        [MaxLength(20)]
        public string Code { get; set; } = "";

        /// <summary>book／product</summary>
        [MaxLength(20)]
        public string Kind { get; set; } = "book";

        [MaxLength(500)]
        public string Title { get; set; } = "";

        /// <summary>作者（書）或品牌（商品），多位以「、」串起</summary>
        [MaxLength(500)]
        public string? Authors { get; set; }

        [MaxLength(300)]
        public string? Publisher { get; set; }

        /// <summary>出版日期，來源格式不一（2026-05、2026-05-01、May 2020），原樣保留</summary>
        [MaxLength(50)]
        public string? PubDate { get; set; }

        /// <summary>業主自己的備註（放哪裡、借給誰…），公開頁不顯示</summary>
        [MaxLength(1000)]
        public string? Note { get; set; }

        /// <summary>查詢當下合併好的完整資料（BookInfo 的 JSON）</summary>
        public string DataJson { get; set; } = "{}";

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
    }
}
