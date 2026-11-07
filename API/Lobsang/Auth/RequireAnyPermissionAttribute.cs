using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Lobsang.Services;

namespace Lobsang.Auth
{
    /// <summary>
    /// 「持有其中任一個權限碼即可」：[RequireAnyPermission("kitchen.menu", "kitchen.nutrition")]。
    ///
    /// ── 為什麼需要它（2026-09-16）───────────────────────────────
    /// 廚務的菜單**同一份資料有兩個入口**：廚師在「菜單與出餐」登打、
    /// 營養師在「菜單清單」回意見，兩邊讀的是同一支 <c>GET menu</c>。
    /// 而 <see cref="RequirePermissionAttribute"/> 只收單一碼 ⇒
    /// 掛 kitchen.menu 營養師就進不來、掛 kitchen.nutrition 廚師就進不來。
    ///
    /// ⚠ **不要改成「開一個共用的讀取碼」** —— 那要動 DbSeeder 的權限字典與所有角色，
    ///   而業主 0817 定案的「營養師只能回意見」正是靠這兩個碼分開才成立的。
    /// ⚠ 寫入端點仍逐一掛 <see cref="RequirePermissionAttribute"/>：
    ///   菜單寫入＝kitchen.menu、意見＝kitchen.nutrition，這個屬性只用在**讀**。
    ///
    /// 判準與單碼版一致：含 system.admin 即全通；403 一樣寫錯誤日誌（安全相關要留軌跡）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class RequireAnyPermissionAttribute : AuthorizeAttribute, IAsyncAuthorizationFilter
    {
        private readonly string[] _codes;

        public RequireAnyPermissionAttribute(params string[] codes) => _codes = codes;

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            var perms = user.FindAll("perm").Select(c => c.Value).ToHashSet();
            if (perms.Contains("system.admin") || _codes.Any(perms.Contains)) return;

            var message = $"沒有執行此操作的權限（需要 {string.Join(" 或 ", _codes)}）";
            context.Result = new ObjectResult(new { message, code = "forbidden" })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };

            var errorLogger = context.HttpContext.RequestServices.GetService<ErrorLogger>();
            if (errorLogger is not null)
                await errorLogger.LogAsync(context.HttpContext, StatusCodes.Status403Forbidden, "forbidden", message);
        }
    }
}
