using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Lobsang.Services;

namespace Lobsang.Auth
{
    /// <summary>
    /// 權限代碼檢查：[RequirePermission("resident.write")]。
    /// 權限以 claim "perm" 存於 JWT；擁有 system.admin 者視同全通過。
    /// 權限不足會寫入錯誤日誌（安全相關，需留軌跡）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class RequirePermissionAttribute : AuthorizeAttribute, IAsyncAuthorizationFilter
    {
        private readonly string _code;

        public RequirePermissionAttribute(string code) => _code = code;

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            var perms = user.FindAll("perm").Select(c => c.Value).ToHashSet();
            if (perms.Contains(_code) || perms.Contains("system.admin")) return;

            var message = $"沒有執行此操作的權限（需要 {_code}）";
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
