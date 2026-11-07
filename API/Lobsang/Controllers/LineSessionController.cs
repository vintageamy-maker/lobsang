using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using Lobsang.Services;

namespace Lobsang.Controllers
{
    public record LineSessionRequest(string IdToken);

    /// <summary>
    /// 用 LINE 身分換一顆**能力被限縮的短命權杖**，讓 LIFF 頁面可以直接呼叫
    /// 既有的後台 API（例如表單引擎 <c>api/admin/work-records</c>）。
    ///
    /// ⚠⚠ 為什麼不做成「匿名端點 ＋ 每支自己用 LINE 身分反查使用者」：
    /// 後台 API 的授權不只是一個權限碼 —— 資料可見範圍、代他人操作、
    /// <c>ApprovalResolver.CanAct</c>（誰能簽）全都讀 <c>perm</c> claim。
    /// 走匿名路線等於把這些判斷各抄一份，**而兩份判斷遲早有一份先過期**。
    ///
    /// ⚠⚠ 為什麼不直接用後台的登入 API 發一顆正常權杖：
    /// 那是 8 小時、全權限的東西。掉手機會從「別人能替你填一張單」變成
    /// **「別人拿到你的整個後台」**。LIFF 綁定頁也是因為這個理由刻意不重用登入 API。
    ///
    /// ⇒ 折衷：**同一套權限機制，但餵給它一組更小的 perm，並且另外加一道路由閘門。**
    /// 三道鎖見 <see cref="LineSessionPolicy"/> 與 <see cref="LineSessionGuard"/>，
    /// 第三道（每次請求重驗 IsActive）在 <c>Program.cs</c> 的 OnTokenValidated。
    /// </summary>
    [ApiController]
    [Route("api/line")]
    [AllowAnonymous]
    public class LineSessionController(
        AppDbContext db,
        LineIdentityService lineIdentity,
        TokenIssuer tokens,
        ILogger<LineSessionController> logger) : ControllerBase
    {
        /// <summary>
        /// 權杖有效期。
        /// ⚠ 刻意很短：LIFF 每次開頁本來就會跟 LINE 要一次 ID Token，
        /// 重換一顆的成本只是一次 HTTP，**短命是免費的**。
        /// ⚠ 但別再縮短 —— 長一點的表單要填好幾分鐘，太短會在填到一半時過期。
        /// </summary>
        static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

        [HttpPost("session")]
        public async Task<IActionResult> Session([FromBody] LineSessionRequest req)
        {
            // ── 1. 身分：只信 LINE 簽出來的 ID Token ──
            // ⚠⚠ 不可改成「看前端送來的 lineUserId」——
            //    那樣任何知道別人 LINE 代號的人都能用他的身分送單。
            var (lineUserId, tokenError) = await lineIdentity.VerifyAsync(req?.IdToken ?? "");
            if (lineUserId is null)
                return BadRequest(new { message = $"LINE 身分驗證失敗：{tokenError}", code = "line_identity" });

            // ⚠ 含 IsActive —— 與 LineEventQueue、LineMessagingClient.NotifyUserAsync 的判定一致：
            //   **有綁定不等於可以用，停用者視同未綁定**。少了這個條件就是繞過帳號停用的後門。
            var user = await db.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role!.RolePermissions).ThenInclude(rp => rp.Permission)
                .FirstOrDefaultAsync(u => u.LineUserId == lineUserId && u.IsActive);

            if (user is null)
                return BadRequest(new
                {
                    needBind = true,
                    message = "這個 LINE 還沒有綁定可用的帳號，請先回聊天室輸入「綁定」完成綁定。",
                    code = "not_bound",
                });

            // ── 2. 能力：自己的權限 ∩ 白名單 ──
            // ⚠ 刻意**不併入代理權限**（approvals.DelegatedPermissionCodesAsync）：
            //   代理的用途是簽核，而 LINE 不做簽核。少一條路少一個洞。
            var own = user.UserRoles
                .SelectMany(ur => ur.Role!.RolePermissions)
                .Select(rp => rp.Permission!.Code)
                .Distinct();
            var granted = own.Where(LineSessionPolicy.AllowedPermissions.Contains).ToList();

            /*
             * ⚠ 一個權限都沒有的人（例如只開了後台唯讀報表的外部帳號）回 **200**，不是 403。
             *   403 在 LIFF 上會變成一個壞掉的畫面，使用者不知道是自己沒開放
             *   還是系統壞了。回 200 並附上 can，讓畫面說一句人話。
             */
            var token = tokens.Create(
                user.Id, user.Name, user.InstitutionId, granted, Lifetime, source: "line",
                tokenVersion: user.TokenVersion);   // 權限／組織異動後 LINE 的舊權杖也要失效

            logger.LogInformation("LINE session 發給 {UserId}（{Name}），權限 {Perms}",
                user.Id, user.Name, granted.Count == 0 ? "（無）" : string.Join(",", granted));

            return Ok(new
            {
                token,
                expiresInSeconds = (int)Lifetime.TotalSeconds,
                userId = user.Id,
                name = user.Name,
                /* ⭐ 給 LIFF 畫面決定要不要顯示入口。新專案在 LineSessionPolicy 加權限時，這裡同步加旗標。
                 * ⚠ 這只是**畫面提示**，真正的邊界在 LineSessionPolicy／LineSessionGuard。 */
                can = new
                {
                    records = granted.Contains("record.write"),      // 填單、送出、上傳附件
                    recordsRead = granted.Contains("record.read"),   // 看單
                },
                // 沒有任何能力時給一句可以直接印在畫面上的話
                note = granted.Count == 0
                    ? "您的角色沒有開放 LINE 上的功能，請洽管理員。"
                    : null,
            });
        }
    }
}
