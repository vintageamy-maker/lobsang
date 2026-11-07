using System.Security.Claims;

namespace Lobsang.Auth
{
    /// <summary>
    /// 在 Controller 內做細部權限判斷用 —— 同一個端點對「自己」與「別人」規則不同時。
    ///
    /// 例：班表確認的端點，同仁確認自己的只要 schedule.read，
    /// 代別人確認才需要 schedule.write，無法用單一 [RequirePermission] 表達。
    ///
    /// 判準與 <see cref="RequirePermissionAttribute"/> 一致：含 system.admin 即全通。
    /// </summary>
    public static class PermissionClaims
    {
        public static bool HasPermission(this ClaimsPrincipal user, string code)
        {
            var perms = user.FindAll("perm").Select(c => c.Value).ToHashSet();
            return perms.Contains(code) || perms.Contains("system.admin");
        }

        /// <summary>
        /// 持有其中任一個權限碼即可。
        ///
        /// ⚠ 用在「同一份資料有兩個入口」的地方 —— 廚務菜單的照片就是：
        /// 廚師（kitchen.menu）與營養師（kitchen.nutrition）看的是同一批照片，
        /// 而 <see cref="FileAccessPolicy"/> 原本只給得出單一讀取碼。
        /// 屬性版見 <see cref="RequireAnyPermissionAttribute"/>。
        /// </summary>
        public static bool HasAnyPermission(this ClaimsPrincipal user, params string[] codes)
        {
            var perms = user.FindAll("perm").Select(c => c.Value).ToHashSet();
            return perms.Contains("system.admin") || codes.Any(perms.Contains);
        }
    }
}
