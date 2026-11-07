using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    /// <summary>
    /// 檔案分類。**封閉清單**，因為它會成為儲存路徑的一段 ——
    /// 路徑上的任何一段都不可以來自使用者輸入（`../` 與同名覆蓋由此根絕）。
    ///
    /// 分類同時決定兩件事：存到哪個前綴，以及要有什麼權限才碰得到
    /// （見 <c>FileAccessPolicy</c>）。
    ///
    /// ⭐ 新專案加分類：⚠ **一律加在最後**，不可插隊改動既有序號（資料庫存的是數字）；
    ///   並在 FileAccessPolicy 補上讀寫權限碼。
    /// ⚠⚠ 敏感資料（個資、病歷、考核…）**不可沿用 Record**：record.read 通常全員都有，
    ///   掛上去等於全公司看得到。要開獨立分類配自己的權限碼。
    /// </summary>
    public enum FileCategory
    {
        /// <summary>表單附件</summary>
        Record,
        /// <summary>人事文件（證照、到職資料）—— 員工個資</summary>
        Hr,
        /// <summary>系統產生的匯出包 —— 可重新產生，可定期清除</summary>
        Export,
        /// <summary>機構設定用（logo、範本檔）</summary>
        System,
    }

    public enum StorageBackend
    {
        /// <summary>主機本機檔案系統（民生醫院；目前無對外雲端網路）</summary>
        Local,
        /// <summary>Google Cloud Storage</summary>
        Gcs,
    }

    /// <summary>
    /// 已上傳檔案的主檔。
    ///
    /// ⚠ **檔案本身不進資料庫，資料庫只存它的身分證。**
    /// 三個欄位是刻意分開的：
    /// <list type="bullet">
    /// <item><see cref="ObjectKey"/> 是儲存路徑，檔名一律 GUID；</item>
    /// <item><see cref="OriginalName"/> 是使用者看到的名字，**永遠不進路徑**
    ///   —— 中文檔名、同名覆蓋、路徑穿越都由這一刀根絕；</item>
    /// <item><see cref="Backend"/> 是**存進去當下**用的後端，不是「現在的設定」。
    ///   機構日後從本機搬到雲端時，靠這一欄才知道舊檔要去哪裡找。</item>
    /// </list>
    /// </summary>
    public class StoredFile : IAuditable, ISoftDelete
    {
        public int Id { get; set; }
        public int InstitutionId { get; set; }

        public FileCategory Category { get; set; }

        /// <summary>
        /// 儲存路徑：<c>inst-{機構}/{分類}/{yyyy}/{MM}/{GUID}{副檔名}</c>。
        /// ⚠ 機構代號在最前面 —— 多租戶的隔離靠路徑，不只靠查詢條件。
        /// </summary>
        [MaxLength(300)]
        public string ObjectKey { get; set; } = string.Empty;

        public StorageBackend Backend { get; set; }

        /// <summary>使用者原本的檔名（可能是中文、可能重複）。只用於顯示與下載時的檔名</summary>
        [MaxLength(255)]
        public string OriginalName { get; set; } = string.Empty;

        /// <summary>
        /// 內容型別。⚠ **以檔頭位元組驗出來的**，不是瀏覽器送來的 Content-Type，
        /// 也不是從副檔名猜的 —— 那兩個都由上傳端控制，等於沒有驗。
        /// </summary>
        [MaxLength(100)]
        public string ContentType { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        /// <summary>內容雜湊。用於重複上傳偵測與日後的完整性稽核</summary>
        [MaxLength(64)]
        public string Sha256 { get; set; } = string.Empty;

        // ===== 歸屬 =====
        // 多型關聯，刻意不做外鍵：附件會掛在院童、工作記錄、加班單、危機事件……
        // 每多一種就加一個可空外鍵，資料表會被撐爛。
        // 代價是沒有資料庫層的參照完整性，由呼叫端負責。

        /// <summary>掛在哪一種單據（<c>resident</c>／<c>work_record</c>／<c>overtime</c>…）</summary>
        [MaxLength(50)]
        public string? OwnerType { get; set; }
        public int? OwnerId { get; set; }

        /// <summary>用途註記（例如「身分證正面」「事故現場照」）</summary>
        [MaxLength(200)]
        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
