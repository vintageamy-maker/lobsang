using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Lobsang.Services;
using System.Security.Claims;

namespace Lobsang.Auth
{
    /// <summary>
    /// 認證密碼（二次密碼）檢查：<c>[RequireStepUp]</c>。
    ///
    /// ⭐ **這是那道鎖真正的位置。** 前端的跳窗只是入口 ——
    /// 若只在畫面擋，把瀏覽器裡的旗標改成 true 就能繼續向 API 取薪資與身分證號，
    /// 那道鎖看起來是鎖、實際是貼紙。
    ///
    /// 判準（業主 2026-08-14 拍板）：**只擋看得到個人金額的頁**。
    /// 法定費率、薪資科目、薪資結構、出勤規則等沒有個人金額，不擋。
    ///
    /// ⚠ 這道檢查**疊在權限之上**，不是取代：先有 payroll 權限，再過認證密碼。
    ///    回 403 並帶 <c>step_up_required</c>，前端據此跳出認證視窗。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class RequireStepUpAttribute : AuthorizeAttribute, IAsyncAuthorizationFilter
    {
        public const string HeaderName = "X-StepUp-Token";

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var http = context.HttpContext;
            var principal = http.User;
            if (principal.Identity?.IsAuthenticated != true)
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            var idText = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idText, out var userId))
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            var svc = http.RequestServices.GetService<StepUpService>();
            if (svc is null) return;   // 未註冊服務時不擋（開發期保護，避免整站掛掉）

            var token = http.Request.Headers[HeaderName].FirstOrDefault();
            var session = await svc.TouchAsync(userId, token);
            if (session is not null)
            {
                // 讓回應帶回剩餘時間 —— 前端的倒數以**後端**為準。
                //
                // ⚠ 回「還剩幾秒」，不回絕對時間。容器跑 UTC、使用者的瀏覽器在 UTC+8，
                //    而 DateTime 序列化出來**沒有時區標記**，前端會當成本地時間解析 ——
                //    整整差 8 小時，倒數一開始就是負的，畫面立刻判定逾時把人踢回輸入密碼，
                //    但後端其實還通著。秒數沒有時區，怎麼擺都不會錯。
                var seconds = (int)Math.Max(0, (session.ExpiresAt - DateTime.Now).TotalSeconds);
                http.Response.Headers["X-StepUp-Expires-In"] = seconds.ToString();
                return;
            }

            var hasPassword = await svc.HasPasswordAsync(userId);
            context.Result = new ObjectResult(new
            {
                message = hasPassword
                    ? "此功能需要通過認證密碼"
                    : "尚未設定認證密碼，請先到「我的認證密碼」設定",
                code = hasPassword ? "step_up_required" : "step_up_not_set",
            })
            { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
