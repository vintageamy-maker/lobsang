namespace Lobsang.Models
{
    /// <summary>
    /// 認證密碼（二次密碼）通過後的解鎖工作階段。
    ///
    /// ⭐ **這張表是這道鎖真正的門。** 畫面上的跳窗只是入口 ——
    /// 若只在前端擋，把瀏覽器的旗標改成 true 就能繼續向 API 取薪資與身分證號，
    /// 那道鎖看起來是鎖、實際是貼紙。因此受保護的端點一律回頭查這張表。
    ///
    /// ⭐ 一次認證，所有受保護的頁共用（業主指定）——
    /// 所以工作階段掛在「人」身上，不掛在頁面上。
    ///
    /// ⚠ 逾時是**閒置**逾時（滑動視窗）：每次通過檢查就把 <see cref="ExpiresAt"/> 往後推。
    ///    倒數以本表為準，前端的計時器只是顯示用。
    /// </summary>
    public class StepUpSession
    {
        public int Id { get; set; }
        public int InstitutionId { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }

        /// <summary>隨機工作階段代碼。⚠ 只在建立當下回傳一次，之後只用於比對</summary>
        public string Token { get; set; } = "";

        public DateTime CreatedAt { get; set; }
        /// <summary>最後一次通過檢查的時間 —— 稽核要看得出這段解鎖期間有沒有在動</summary>
        public DateTime LastActivityAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        /// <summary>按下「認證密碼登出」或被系統管理員重設密碼時作廢</summary>
        public DateTime? RevokedAt { get; set; }

        /// <summary>建立來源，供稽核追（IP／裝置）</summary>
        public string? UserAgent { get; set; }
        public string? IpAddress { get; set; }

        public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
    }
}
