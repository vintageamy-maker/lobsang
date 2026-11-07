using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Lobsang.Services
{
    /// <summary>
    /// JWT 產生器。**全系統只有這一份**。
    ///
    /// ⚠⚠ 為什麼要抽出來：2026-09-13 新增 LINE 專用的短命權杖時，最省事的作法是
    /// 在 <c>LineSessionController</c> 裡複製一份 <c>CreateToken</c>。
    /// 那樣一來，日後任何一次改動（加一個 claim、換簽章演算法、調整 issuer）
    /// 都會有一邊被忘記，而**兩邊不一致的症狀是「某些人在某些入口突然沒有權限」**，
    /// 完全看不出根因。同樣的教訓 <c>LineIdentityService</c> 當初就記過一次。
    ///
    /// ⚠ <c>inst</c> claim **一定要有**。全專案的 controller 都寫
    /// <c>int.Parse(User.FindFirstValue("inst")!)</c> —— 少了它不是 403，是 **500**。
    /// </summary>
    public class TokenIssuer(IConfiguration config)
    {
        /// <summary>JWT 的 issuer／audience。⭐ 新專案改成自己的名字（Program.cs 驗證端也讀這個常數）</summary>
        public const string Issuer = "Lobsang";

        /// <summary>後台登入用的預設有效期（小時）。未設定時 8 小時。</summary>
        public int DefaultExpireHours =>
            int.TryParse(config["Jwt:ExpireHours"], out var h) ? h : 8;

        /// <param name="lifetime">有效期。後台登入用 <see cref="DefaultExpireHours"/>；LINE 用 15 分鐘。</param>
        /// <param name="source">
        /// 發給誰用的。<c>null</c>＝後台登入（既有行為，不加這個 claim）；
        /// <c>"line"</c>＝LIFF 專用，會被 <see cref="Auth.LineSessionGuard"/> 限制只能打白名單上的路由。
        /// ⚠ 這個值是**能力的標記，不是身分的標記** —— 判斷「是誰」一律用 NameIdentifier。
        /// </param>
        public string Create(
            int userId, string name, int institutionId,
            IEnumerable<string> permissions,
            TimeSpan? lifetime = null,
            string? source = null,
            int? tokenVersion = null)
        {
            var key = config["Jwt:Key"]
                ?? throw new InvalidOperationException("缺少 Jwt:Key 設定（可用環境變數 Jwt__Key 提供）");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Name, name),
                new("inst", institutionId.ToString()),
            };
            claims.AddRange(permissions.Select(p => new Claim("perm", p)));
            if (!string.IsNullOrEmpty(source)) claims.Add(new Claim("src", source));
            // ⭐ 憑證版本（2026-10-09）：組織異動生效時把受影響的人 +1，舊權杖就作廢（見 Program.cs OnTokenValidated）
            if (tokenVersion is int tv) claims.Add(new Claim("tv", tv.ToString()));

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: Issuer,
                claims: claims,
                expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(DefaultExpireHours)),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
