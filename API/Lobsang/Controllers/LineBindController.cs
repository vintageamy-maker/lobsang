using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Data;
using Lobsang.Models;
using Lobsang.Services;

namespace Lobsang.Controllers
{
    public record LineBindRequest(string IdToken, string Account, string Password);
    public record LineTokenRequest(string IdToken);

    /// <summary>
    /// LINE 帳號綁定（LIFF 自助）。
    ///
    /// ⚠ **這支必須匿名** —— 綁定的當下使用者還沒有登入權杖，那正是要解決的問題。
    ///    身分由兩邊各自證明：LINE 端用 LIFF 的 ID Token，我方用帳號密碼。
    ///
    /// ⚠ 匿名 ＋ 收密碼 ⇒ 這是一個**可被拿來猜密碼的端點**。
    ///    因此：錯誤訊息一律含糊、不透露帳號是否存在、每次嘗試都留紀錄。
    /// </summary>
    [ApiController]
    [Route("api/line")]
    [AllowAnonymous]
    public class LineBindController(
        AppDbContext db, IConfiguration config, LineIdentityService lineIdentity,
        ILogger<LineBindController> logger) : ControllerBase
    {
        /// <summary>前端要用的設定（LIFF ID）。⚠ LIFF ID 不是機密，可公開</summary>
        [HttpGet("bind-config")]
        public IActionResult Config() => Ok(new
        {
            liffId = config["Line:LiffId"],
            configured = !string.IsNullOrWhiteSpace(config["Line:LiffId"]),
        });

        /// <summary>
        /// 這個 LINE 目前綁到誰。給 LIFF 頁進來時先問一次，決定要顯示「綁定表單」還是「解除綁定」。
        /// </summary>
        [HttpPost("bind-status")]
        public async Task<IActionResult> Status([FromBody] LineTokenRequest req)
        {
            var (lineUserId, err) = await lineIdentity.VerifyAsync(req.IdToken);
            if (lineUserId is null) return BadRequest(new { message = $"LINE 身分驗證失敗：{err}" });

            // ⚠ 這裡**不加 IsActive 條件**：停用者要看得到自己綁在哪、也要能解綁。
            //    「能不能用功能」與「綁在誰身上」是兩件事，混在一起會讓離職者卡住解不掉。
            var user = await db.Users.FirstOrDefaultAsync(u => u.LineUserId == lineUserId);
            return Ok(new
            {
                bound = user is not null,
                name = user?.Name,
                employeeNo = user?.EmployeeNo,
                active = user?.IsActive,
                boundAt = user?.LineBoundAt,
            });
        }

        /// <summary>
        /// 本人自助解除綁定。
        ///
        /// ⚠ **刻意不要求密碼**。呼叫者已經用 ID Token 證明持有這個 LINE 帳號，
        ///    而解綁只是移除自己的存取，不會取得任何額外權限 ——
        ///    真正需要防的是「重新綁定」，那一步仍然要帳號密碼。
        ///    在這裡要密碼只會讓「換手機」這種正當情境變麻煩。
        /// </summary>
        [HttpPost("unbind")]
        public async Task<IActionResult> Unbind([FromBody] LineTokenRequest req)
        {
            var (lineUserId, err) = await lineIdentity.VerifyAsync(req.IdToken);
            if (lineUserId is null) return BadRequest(new { message = $"LINE 身分驗證失敗：{err}" });

            var user = await db.Users.FirstOrDefaultAsync(u => u.LineUserId == lineUserId);
            if (user is null) return Ok(new { message = "這個 LINE 本來就沒有綁定", already = true });

            /*
             * ⚠⚠ 這裡要存 **UTC**，不是 UTC+8：
             *   LineBoundAt／LastLoginAt／稽核／錯誤日誌 → 存 UTC，
             *   前端加 'Z' 再換算成當地時間顯示（Users.tsx 的 lineBoundAt 就是）。
             *
             * ⇒ 這一支若改成 UtcNow.AddHours(8)，畫面上的綁定時間會變成 **8 小時後**。
             *
             * 原本寫 DateTime.Now，在容器（UTC 時區）下結果正確，但那是**碰巧**——
             * 只要有人把容器時區設成 Asia/Taipei 就會無聲壞掉。改成明寫 UtcNow。
             */
            user.LineUserId = null;
            user.LineBoundAt = null;
            db.LineBindLogs.Add(new LineBindLog
            {
                InstitutionId = user.InstitutionId,
                UserId = user.Id,
                LineUserId = lineUserId,
                Action = LineBindAction.Unbind,
                ActedAt = DateTime.UtcNow,
                ActedBy = user.Id,           // 自助：執行者是本人
                ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Reason = "自 LIFF 自助解除",
            });
            await db.SaveChangesAsync();
            return Ok(new { message = "已解除綁定", name = user.Name, already = false });
        }

        [HttpPost("bind")]
        public async Task<IActionResult> Bind([FromBody] LineBindRequest req)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

            // ── 1. 驗 LINE 端身分 ──
            var (lineUserId, tokenError) = await lineIdentity.VerifyAsync(req.IdToken);
            if (lineUserId is null)
            {
                logger.LogWarning("LINE 綁定失敗（ID Token）：{Err} from {Ip}", tokenError, ip);
                return BadRequest(new { message = $"LINE 身分驗證失敗：{tokenError}" });
            }

            // ── 2. 驗我方身分 ──
            var user = await db.Users.FirstOrDefaultAsync(
                u => u.Account == req.Account && u.IsActive);

            // ⚠ 帳號不存在、密碼錯、帳號停用一律回同一句 ——
            //    分開講等於送給攻擊者一支「這個帳號存不存在」的查詢工具
            if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            {
                logger.LogWarning("LINE 綁定失敗（帳密）：account={Account} from {Ip}", req.Account, ip);
                return Unauthorized(new { message = "帳號或密碼錯誤" });
            }

            // ── 3. 雙向唯一 ──
            var lineTaken = await db.Users.FirstOrDefaultAsync(
                u => u.LineUserId == lineUserId && u.Id != user.Id);
            if (lineTaken is not null)
                return Conflict(new
                {
                    message = $"這個 LINE 帳號已經綁定在「{lineTaken.Name}」身上。"
                            + "若要改綁，請先請管理員解除原本的綁定。",
                });

            var isRebind = user.LineUserId is not null && user.LineUserId != lineUserId;
            if (user.LineUserId == lineUserId)
                return Ok(new { message = $"已經綁定過了（{user.Name}）", user.Name, already = true });

            // ── 4. 綁定 ──
            var old = user.LineUserId;
            user.LineUserId = lineUserId;
            user.LineBoundAt = DateTime.UtcNow;

            db.LineBindLogs.Add(new LineBindLog
            {
                InstitutionId = user.InstitutionId,
                UserId = user.Id,
                LineUserId = lineUserId,
                Action = isRebind ? LineBindAction.Rebind : LineBindAction.Bind,
                ActedAt = DateTime.UtcNow,
                ActedBy = user.Id,          // 自助綁定：執行者就是本人
                ClientIp = ip,
                Reason = isRebind ? $"自 LIFF 改綁（原 {old}）" : "自 LIFF 綁定",
            });

            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // 唯一索引撞上＝同時有另一個請求綁了同一個 LINE。回明確訊息而不是 500
                return Conflict(new { message = "這個 LINE 帳號剛剛已被綁定，請重新整理再試" });
            }

            return Ok(new { message = "綁定成功", user.Name, employeeNo = user.EmployeeNo, already = false });
        }
    }
}
