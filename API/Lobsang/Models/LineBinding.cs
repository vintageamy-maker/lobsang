using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    public enum LineBindAction { Bind, Unbind, Rebind }

    /// <summary>
    /// LINE 綁定／解綁的軌跡。
    ///
    /// ⚠ 綁定是**身分變更** —— 綁定之後那個 LINE 帳號就能以這個人的名義填單、收通知。
    ///    因此每一次變更都要留得下「誰、何時、從哪裡、為什麼」，
    ///    而且**解綁也要留**（不是把 users 那一欄清掉就算）。
    /// </summary>
    public class LineBindLog
    {
        public int Id { get; set; }
        public int InstitutionId { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        [MaxLength(64)]
        public string LineUserId { get; set; } = string.Empty;

        public LineBindAction Action { get; set; }

        public DateTime ActedAt { get; set; }

        /// <summary>
        /// 執行者。⚠ 自助綁定時**等於 UserId 本人**；後台代為解綁時是管理員。
        /// 兩者要分得出來，否則查不出「這個綁定是誰弄的」。
        /// </summary>
        public int? ActedBy { get; set; }

        [MaxLength(45)]
        public string? ClientIp { get; set; }

        [MaxLength(200)]
        public string? Reason { get; set; }
    }
}
