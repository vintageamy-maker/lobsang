using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lobsang.Auth;
using Lobsang.Services;
using System.Security.Claims;

namespace Lobsang.Controllers
{
    public record StepUpUnlockRequest(string Password);
    public record SetStepUpPasswordRequest(string? CurrentPassword, string NewPassword);
    public record ResetStepUpRequest(int UserId, string NewPassword);

    /// <summary>
    /// 認證密碼（二次密碼）。⭐ 一次認證，所有受保護的頁共用。
    /// </summary>
    [ApiController]
    [Route("api/auth/step-up")]
    [Authorize]
    public class StepUpController(StepUpService svc) : ControllerBase
    {
        int InstitutionId => int.Parse(User.FindFirstValue("inst")!);
        int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        string? Token => Request.Headers[RequireStepUpAttribute.HeaderName].FirstOrDefault();

        /// <summary>目前狀態：有沒有設密碼、有沒有解鎖中、還剩多久</summary>
        [HttpGet("status")]
        public async Task<IActionResult> Status()
        {
            var session = await svc.TouchAsync(UserId, Token);
            return Ok(new
            {
                hasPassword = await svc.HasPasswordAsync(UserId),
                unlocked = session is not null,
                // ⚠ 回「還剩幾秒」而非絕對時間 —— 容器 UTC、瀏覽器 UTC+8，
                //    序列化的 DateTime 沒有時區標記，前端會差 8 小時（見 RequireStepUpAttribute）
                expiresInSeconds = session is null ? 0 : (int)Math.Max(0, (session.ExpiresAt - DateTime.Now).TotalSeconds),
                idleMinutes = (int)StepUpService.IdleTimeout.TotalMinutes,
                // 前端據此決定要不要顯示設定表單 —— 沒資格的人看到表單卻存不了最惱人
                eligible = MayHaveStepUp(),
                // 前端要拿去顯示規則說明 —— ⚠ 規則只寫一次，畫面不要各寫各的
                passwordRule = "至少 8 碼，且必須同時包含英文字母與數字",
            });
        }

        [HttpPost]
        public async Task<IActionResult> Unlock(StepUpUnlockRequest r)
        {
            var (error, token, expiresAt) = await svc.UnlockAsync(
                InstitutionId, UserId, r.Password ?? "",
                Request.Headers.UserAgent.FirstOrDefault(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return error is null
                ? Ok(new { token, expiresInSeconds = expiresAt is DateTime e ? (int)Math.Max(0, (e - DateTime.Now).TotalSeconds) : 0 })
                : BadRequest(new { message = error, code = "step_up_failed" });
        }

        /// <summary>認證密碼登出 —— 立即作廢，不必等閒置逾時</summary>
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await svc.RevokeAsync(UserId, Token);
            return Ok(new { ok = true });
        }

        /// <summary>
        /// 誰可以擁有認證密碼。
        ///
        /// 業主指定「只限系統管理員與人事」。⚠ 系統裡**沒有獨立的「人事」權限碼** ——
        /// 人事實際上是靠 <c>payroll.*</c> 這組碼，所以判準取
        /// 「系統管理員 或 擁有任一 payroll 權限」。
        ///
        /// ⚠ 不開放給全體：只有碰得到薪資的人才需要這道密碼，
        ///    其他人設了也用不到，徒增一個沒人維護的密碼。
        /// </summary>
        bool MayHaveStepUp() =>
            User.FindAll("perm").Select(c => c.Value)
                .Any(p => p == "system.admin" || p.StartsWith("payroll."));

        IActionResult NotEligible() => StatusCode(StatusCodes.Status403Forbidden, new
        {
            message = "只有系統管理員、人事與薪資簽核者需要設定認證密碼",
            code = "step_up_not_eligible",
        });

        /// <summary>設定／變更自己的認證密碼</summary>
        [HttpPost("password")]
        public async Task<IActionResult> SetPassword(SetStepUpPasswordRequest r)
        {
            if (!MayHaveStepUp()) return NotEligible();

            var error = await svc.SetPasswordAsync(UserId, r.CurrentPassword, r.NewPassword ?? "");
            return error is null
                ? Ok(new { ok = true })
                : BadRequest(new { message = error, code = "invalid_password" });
        }

        /// <summary>
        /// 系統管理員重設他人的認證密碼（「忘記密碼」的解法）。
        /// ⚠ 需要 <c>system.admin</c>，且一定會留稽核紀錄。
        /// </summary>
        [HttpPost("reset")]
        [RequirePermission("system.admin")]
        public async Task<IActionResult> Reset(ResetStepUpRequest r)
        {
            var error = await svc.ResetByAdminAsync(r.UserId, r.NewPassword ?? "", UserId);
            return error is null
                ? Ok(new { ok = true })
                : BadRequest(new { message = error, code = "invalid_password" });
        }
    }
}
