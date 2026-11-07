using Microsoft.EntityFrameworkCore;
using Lobsang.Data;
using Lobsang.Models;
using System.Security.Cryptography;

namespace Lobsang.Services
{
    /// <summary>
    /// 認證密碼（二次密碼）。
    ///
    /// ⭐ 設計上最重要的一件事：**這道鎖由後端認，不是由畫面認。**
    /// 前端的跳窗只是入口；受保護的端點一律回頭查解鎖工作階段，
    /// 否則把瀏覽器的旗標改掉就能繼續取薪資與身分證號。
    /// </summary>
    public class StepUpService(AppDbContext db, AuditLogger audit)
    {
        /// <summary>閒置逾時。⚠ 滑動視窗：每次通過檢查就往後推</summary>
        public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

        /// <summary>
        /// 認證密碼的強度規範。
        /// ⚠ 登入密碼是 8 碼；這道保護的是薪資與身分證號，**不可比登入寬**。
        /// </summary>
        public static string? ValidatePassword(string? pw)
        {
            if (string.IsNullOrWhiteSpace(pw) || pw.Length < 8)
                return "認證密碼至少 8 碼";
            if (!pw.Any(char.IsLetter) || !pw.Any(char.IsDigit))
                return "認證密碼必須同時包含英文字母與數字";
            return null;
        }

        static string NewToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        /// <summary>有沒有設過認證密碼</summary>
        public async Task<bool> HasPasswordAsync(int userId) =>
            await db.Users.AnyAsync(u => u.Id == userId && u.StepUpPasswordHash != null);

        /// <summary>
        /// 設定或變更自己的認證密碼。
        /// ⚠ 已設定過的要驗舊密碼 —— 否則有人趁別人離開座位就把密碼換掉。
        /// ⚠ 變更後**作廢所有既有的解鎖工作階段**。
        /// </summary>
        public async Task<string?> SetPasswordAsync(int userId, string? current, string next)
        {
            if (ValidatePassword(next) is string bad) return bad;

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return "查無此使用者";

            if (user.StepUpPasswordHash is not null)
            {
                if (string.IsNullOrEmpty(current)) return "請輸入目前的認證密碼";
                if (!BCrypt.Net.BCrypt.Verify(current, user.StepUpPasswordHash))
                    return "目前的認證密碼不正確";
            }

            user.StepUpPasswordHash = BCrypt.Net.BCrypt.HashPassword(next);
            await RevokeAllAsync(userId, "認證密碼已變更");
            await db.SaveChangesAsync();
            audit.Log("SetPassword", "StepUp", userId, null, new { note = "設定／變更認證密碼" });
            return null;
        }

        /// <summary>系統管理員重設他人的認證密碼（供「忘記密碼」）。⚠ 一定要留稽核。</summary>
        public async Task<string?> ResetByAdminAsync(int targetUserId, string next, int actorId)
        {
            if (ValidatePassword(next) is string bad) return bad;

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId);
            if (user is null) return "查無此使用者";

            user.StepUpPasswordHash = BCrypt.Net.BCrypt.HashPassword(next);
            await RevokeAllAsync(targetUserId, "認證密碼被管理者重設");
            await db.SaveChangesAsync();
            audit.Log("ResetByAdmin", "StepUp", targetUserId, null,
                new { note = $"由使用者 #{actorId} 重設認證密碼" });
            return null;
        }

        /// <summary>
        /// 驗證認證密碼並建立解鎖工作階段。
        /// </summary>
        public async Task<(string? Error, string? Token, DateTime? ExpiresAt)> UnlockAsync(
            int institutionId, int userId, string password, string? ua, string? ip)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return ("查無此使用者", null, null);
            if (user.StepUpPasswordHash is null)
                return ("尚未設定認證密碼，請先到「我的認證密碼」設定", null, null);

            if (!BCrypt.Net.BCrypt.Verify(password, user.StepUpPasswordHash))
            {
                audit.Log("UnlockFailed", "StepUp", userId, null, new { note = "認證密碼錯誤" });
                return ("驗證碼錯誤，請重新輸入", null, null);
            }

            // 同一人重新認證時，先作廢舊的 —— 一個人同時只留一個解鎖工作階段
            await RevokeAllAsync(userId, "重新認證");

            var now = DateTime.Now;
            var session = new StepUpSession
            {
                InstitutionId = institutionId,
                UserId = userId,
                Token = NewToken(),
                CreatedAt = now,
                LastActivityAt = now,
                ExpiresAt = now.Add(IdleTimeout),
                UserAgent = ua?.Length > 256 ? ua[..256] : ua,
                IpAddress = ip,
            };
            db.StepUpSessions.Add(session);
            await db.SaveChangesAsync();
            audit.Log("Unlock", "StepUp", userId, null, new { note = "通過認證密碼" });
            return (null, session.Token, session.ExpiresAt);
        }

        /// <summary>
        /// 檢查並延長。⭐ 受保護端點每次呼叫都會走這裡 —— 這就是那道門。
        /// </summary>
        public async Task<StepUpSession?> TouchAsync(int userId, string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            var now = DateTime.Now;
            var s = await db.StepUpSessions
                .FirstOrDefaultAsync(x => x.UserId == userId && x.Token == token);
            if (s is null || !s.IsActive(now)) return null;

            // 滑動視窗：有動作就往後推。⚠ 每次都寫 DB 太吵，差距超過一分鐘才寫
            if ((now - s.LastActivityAt).TotalMinutes >= 1)
            {
                s.LastActivityAt = now;
                s.ExpiresAt = now.Add(IdleTimeout);
                await db.SaveChangesAsync();
            }
            return s;
        }

        public async Task RevokeAsync(int userId, string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return;
            var s = await db.StepUpSessions
                .FirstOrDefaultAsync(x => x.UserId == userId && x.Token == token && x.RevokedAt == null);
            if (s is null) return;
            s.RevokedAt = DateTime.Now;
            await db.SaveChangesAsync();
            audit.Log("Logout", "StepUp", userId, null, new { note = "認證密碼登出" });
        }

        /// <summary>
        /// ⭐ **登入時作廢該使用者所有既有的解鎖工作階段。**
        ///
        /// ⚠ 這是這道門真正的保障。前端登出時清掉 sessionStorage 只是禮貌 ——
        /// 憑證一旦發出去，有人自己留著就還能用。實測過：
        /// 登出（前端只清 localStorage 的 JWT）→ 重新登入 → 用**舊的**認證憑證
        /// 仍然存取得到薪資，而且倒數被刷新回 30 分 —— **第二道門形同虛設**。
        ///
        /// 因此改為：**每次登入都要求重新輸入認證密碼**。
        /// 第二道門的意義就在於「重新進入這個區域要再驗一次」。
        /// </summary>
        public async Task RevokeOnLoginAsync(int userId)
        {
            await RevokeAllAsync(userId, "重新登入");
            await db.SaveChangesAsync();
        }

        async Task RevokeAllAsync(int userId, string reason)
        {
            var rows = await db.StepUpSessions
                .Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync();
            var now = DateTime.Now;
            foreach (var r in rows) r.RevokedAt = now;
            if (rows.Count > 0)
                audit.Log("RevokeAll", "StepUp", userId, null,
                    new { note = $"{reason}，作廢 {rows.Count} 個解鎖工作階段" });
        }
    }
}
