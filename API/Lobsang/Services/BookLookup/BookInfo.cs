namespace Lobsang.Services.BookLookup
{
    /// <summary>
    /// 一本書（或一個商品）合併後的資料。查詢結果、書單的 DataJson 都用這個形狀。
    /// ⚠ 欄位名稱會被前端與資料庫（DataJson）直接使用，改名要一起改。
    /// </summary>
    public class BookInfo
    {
        public string Kind { get; set; } = "book";          // book／product
        public string Code { get; set; } = "";              // ISBN-13 或條碼
        public string? Isbn10 { get; set; }
        public string? Region { get; set; }                 // jp／tw／west／product
        public string? RegionLabel { get; set; }
        public string? Title { get; set; }
        public string? Subtitle { get; set; }
        public List<string> Authors { get; set; } = [];
        public string? Publisher { get; set; }
        public string? Date { get; set; }
        public string? Edition { get; set; }
        public int? Pages { get; set; }
        public string? Price { get; set; }
        /// <summary>售價（打折後）。目前只有誠品線上有，由前端在瀏覽器查到後填入（2026-11-07）</summary>
        public string? SalePrice { get; set; }
        public List<string> Categories { get; set; } = [];
        public string? Language { get; set; }
        public string? Description { get; set; }
        public string? Toc { get; set; }
        public List<string> Covers { get; set; } = [];
        public List<string> Sources { get; set; } = [];
        // 商品才有
        public string? Brand { get; set; }
        public string? Quantity { get; set; }
        public string? Countries { get; set; }
        public string? Ingredients { get; set; }
        public string? Gs1 { get; set; }
    }

    /// <summary>單一來源的查詢狀態：ok＝有資料、none＝查無、err＝出錯（note 說明原因）</summary>
    public record SourceStatus(string Name, string State, string? Note = null);

    public record LinkItem(string Label, string Url);

    /// <summary>不能自動查的條碼（雜誌、日本書下條碼、店內碼、優惠券）顯示的說明卡</summary>
    public record InfoCard(string Title, string? Message, List<string[]> Rows, List<LinkItem> Links);

    public class LookupResult
    {
        public string? Kind { get; set; }
        public string? Code { get; set; }
        public List<string> Notes { get; set; } = [];
        /// <summary>號碼本身有問題（長度、檢查碼）時的訊息</summary>
        public string? Error { get; set; }
        public bool Found { get; set; }
        public BookInfo? Info { get; set; }
        public List<SourceStatus> Statuses { get; set; } = [];
        /// <summary>到其他網站查的連結</summary>
        public List<LinkItem> Links { get; set; } = [];
        public InfoCard? Card { get; set; }
    }
}
