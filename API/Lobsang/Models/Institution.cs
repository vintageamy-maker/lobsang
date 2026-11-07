using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    /// <summary>機構（多租戶預留：現階段僅長愛家園一筆，核心資料表皆掛 InstitutionId）</summary>
    public class Institution : IAuditable
    {
        public int Id { get; set; }

        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(30)]
        public string? ShortName { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 上傳檔案存在哪裡。⚠ **逐機構設定**，不是部署層設定 ——
        /// 育幼院用雲端、民生醫院留本機，但兩家跑在同一個部署上。
        /// 預設 <see cref="StorageBackend.Local"/>：新機構在雲端憑證確認可用之前，
        /// 檔案先落在本機是安全的一邊；反過來會靜默失敗。
        /// </summary>
        public StorageBackend StorageBackend { get; set; } = StorageBackend.Local;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
    }
}
