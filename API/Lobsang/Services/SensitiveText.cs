namespace Lobsang.Services
{
    /// <summary>
    /// 敏感文字的遮罩。
    ///
    /// ⚠⚠ **這不是那道鎖。** 真正的鎖是後端受保護端點的 <c>[RequireStepUp]</c>
    /// ＋ <c>social.case.detail</c> 權限；這裡只負責「一般情況下不要把明碼送出去」。
    /// 遮罩是**減少曝險**，不是存取控制 —— 兩者不可互相取代。
    /// </summary>
    public static class SensitiveText
    {
        /// <summary>⚠ 星號固定四個，見 <see cref="Mask"/> 的說明</summary>
        private const string Stars = "****";

        /// <summary>
        /// 遮成 <c>A12****789</c> 這種樣子：前 3 後 3，中間固定四顆星。
        ///
        /// ⚠⚠ **星號數量固定，不反映真實長度**。
        ///   照長度給星號的話，遮罩本身就洩漏了位數 ——
        ///   而身分證、護照號、銀行帳號的位數各不相同，位數就是一條線索。
        ///
        /// ⚠ 太短的（≤ 6 碼）全部遮掉：留前 3 後 3 會等於沒遮。
        /// ⚠ 空值回 null 而不是一串星號 —— 「沒有填」與「有填但看不到」
        ///   在畫面上必須分得出來，否則使用者會以為資料已經填過了。
        /// </summary>
        public static string? Mask(string? value)
        {
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v)) return null;      // ⚠ 沒填就是沒填
            if (v.Length <= 6) return Stars;
            return v[..3] + Stars + v[^3..];
        }

        /// <summary>這個值是不是已經被遮過了（存檔時用來擋「把遮罩存回去」）</summary>
        /// <remarks>
        /// ⚠⚠ 沒有這道檢查會出事：前端拿到 <c>A12****789</c>，
        /// 使用者沒動那一格就按儲存 ⇒ 遮罩被當成新值寫回資料庫，
        /// **真正的號碼就永久消失了**，而畫面上完全看不出來
        /// （它本來顯示的就是 A12****789）。
        /// </remarks>
        public static bool LooksMasked(string? value) =>
            value is not null && value.Contains(Stars);
    }
}
