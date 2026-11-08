using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    /// <summary>
    /// 採購單（2026-11-08）：採購候選的第二階段。
    /// 流程：採購清單（books）勾選送審 → 建一張採購單、書從清單移過來（明細留下當時的完整資料）
    ///       → 有 purchase.approve 的人逐本核准／退回（退回的回到採購清單）
    ///       → 核准的由員工「上架」：填店內碼、分類、售價、庫存，變成商品（products）
    /// ⭐ 採購單是紀錄：送出後明細內容不再變動（只變狀態），業主要「有紀錄每次的採購清單內容」。
    /// </summary>
    public class PurchaseOrder : IAuditable
    {
        public int Id { get; set; }
        public int InstitutionId { get; set; }

        /// <summary>採購單號，例 PO-20261108-001（同一機構內唯一）</summary>
        [MaxLength(30)]
        public string OrderNo { get; set; } = "";

        /// <summary>送審人（users.id）</summary>
        public int SubmittedBy { get; set; }
        public DateTime SubmittedAt { get; set; }

        /// <summary>送審時寫的說明</summary>
        [MaxLength(1000)]
        public string? Note { get; set; }

        public List<PurchaseOrderItem> Items { get; set; } = [];

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
    }

    /// <summary>採購單明細：一本書一列，送審當下從採購清單複製過來（快照）</summary>
    public class PurchaseOrderItem : IAuditable
    {
        public int Id { get; set; }
        public int PurchaseOrderId { get; set; }
        public PurchaseOrder? PurchaseOrder { get; set; }

        [MaxLength(20)] public string Code { get; set; } = "";
        [MaxLength(20)] public string Kind { get; set; } = "book";
        [MaxLength(500)] public string Title { get; set; } = "";
        [MaxLength(500)] public string? Authors { get; set; }
        [MaxLength(300)] public string? Publisher { get; set; }
        [MaxLength(50)] public string? PubDate { get; set; }

        /// <summary>採購清單上的備註（一起帶過來）</summary>
        [MaxLength(1000)] public string? Note { get; set; }

        /// <summary>送審當下的完整查書資料（BookInfo JSON）</summary>
        public string DataJson { get; set; } = "{}";

        /// <summary>採購數量</summary>
        public int Quantity { get; set; } = 1;

        /// <summary>pending（待審）／approved（核准，待上架）／rejected（退回）／listed（已上架成商品）</summary>
        [MaxLength(20)] public string Status { get; set; } = PurchaseItemStatus.Pending;

        /// <summary>退回原因</summary>
        [MaxLength(500)] public string? RejectReason { get; set; }

        /// <summary>核准／退回的人與時間</summary>
        public int? DecidedBy { get; set; }
        public DateTime? DecidedAt { get; set; }

        /// <summary>上架後對應的商品</summary>
        public int? ProductId { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
    }

    public static class PurchaseItemStatus
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Rejected = "rejected";
        public const string Listed = "listed";
    }
}
