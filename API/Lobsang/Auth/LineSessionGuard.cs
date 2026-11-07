using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Lobsang.Services;

namespace Lobsang.Auth
{
    /// <summary>
    /// LINE 專用權杖的**路由閘門**。只對帶 <c>src=line</c> 的權杖生效，
    /// 後台登入的權杖完全不受影響（連一次字典查詢都不做）。
    ///
    /// ⚠⚠ 這一道與權限白名單是**兩道互相獨立的鎖**，不是 else if：
    /// 就算有人偽造出一顆帶 <c>system.admin</c> 的 <c>src=line</c> 權杖
    /// （前提是拿得到 Jwt:Key，那已經是另一個等級的事故），
    /// 他仍然只打得到這張清單上的路由。
    ///
    /// ⭐ 比對用**路由樣板**（<c>api/admin/work-records/{id}/submit</c>）而不是實際網址，
    /// 所以 id 換成幾都一樣，也不必自己解析路徑。
    ///
    /// ⚠ 註冊順序：排在 <see cref="Filters.ApiErrorLoggingFilter"/> 之後即可
    /// （它是 -3000，本 filter 用預設 0）。授權類的 filter 會在 action 執行前跑完。
    /// </summary>
    public class LineSessionGuard : IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            // ⭐ 不是 LINE 權杖就直接放行 —— 後台登入的路徑完全不受這道影響
            if (user.Identity?.IsAuthenticated != true) return;
            if (user.FindFirst("src")?.Value != "line") return;

            var method = context.HttpContext.Request.Method;
            var template = (context.ActionDescriptor as ControllerActionDescriptor)
                ?.AttributeRouteInfo?.Template;

            if (LineSessionPolicy.AllowedRoutes.Contains(LineSessionPolicy.Key(method, template)))
                return;

            /*
             * ⚠ 訊息要說得出「為什麼」，而不是只丟一個 403。
             *   這條路最可能被踩到的時機是**開發者自己**在 LIFF 上加了一個新畫面、
             *   卻忘了把端點加進白名單 —— 那時看到「沒有開放給 LINE 使用」
             *   才找得到這個檔案；看到一句泛用的「權限不足」只會去查角色設定。
             */
            var message = $"這個功能沒有開放給 LINE 使用（{method} {template}）。請改用電腦版後台。";
            context.Result = new ObjectResult(new { message, code = "line_scope" })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };

            var errorLogger = context.HttpContext.RequestServices.GetService<ErrorLogger>();
            if (errorLogger is not null)
                await errorLogger.LogAsync(
                    context.HttpContext, StatusCodes.Status403Forbidden, "line_scope", message);
        }
    }
}
