using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Lobsang.Data;
using Lobsang.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Lobsang.Controllers
{
    public record LoginRequest(string Account, string Password);
    public record ChangePasswordRequest(string OldPassword, string NewPassword);
    /// <summary>開發站快速登入用；正式站永遠不會回傳這個東西。</summary>
    public record DemoAccount(string Label, string Account, string Password);

    [ApiController]
    [Route("api/auth")]
    public class AuthController(
        AppDbContext db, IConfiguration config, TokenIssuer tokens,
        ApprovalResolver approvals, DataScopeService scopes,
        StepUpService stepUp, IHostEnvironment env) : ControllerBase
    {
        /// <summary>
        /// 此人在各模組的**可見範圍**（Self／Unit／All），與權限碼一併回傳。
        ///
        /// ⚠ 為什麼要特地回傳，而不讓前端自己看權限碼：
        /// 「看得到誰」不是一個權限碼能表達的 —— 單位主管沒有任何 *.manage 權限，
        /// 但因為身為單位主管而看得到所轄單位（含子單位）。前端若以
        /// <c>hasPerm('xxx.manage')</c> 當作「看不看得到別人」，單位主管就會被判成
        /// 只看得到自己，明明後端願意給他整個單位 ——「能力 vs 範圍」的混淆。
        ///
        /// 判準與各模組實際過濾資料時用的 manageCode 必須一致，否則畫面顯示的範圍
        /// 與真正撈得到的資料會對不起來。
        /// </summary>
        private async Task<object> ScopesOfAsync(ClaimsPrincipal who, int institutionId) => new
        {
            // ⭐ 新專案有「看得到誰」的模組（請假、差勤…）時在這裡各加一行，manageCode 與該模組過濾資料用的一致
            users = (await scopes.ScopeOfAsync(who, institutionId, "user.manage")).ToString(),
        };

        /// <summary>
        /// 登入當下 <see cref="ControllerBase.User"/> 還是匿名的（權杖才剛簽出去、下一次請求才會帶著），
        /// 所以要用即將寫進權杖的那組宣告先組一個等價的主體來問範圍。
        /// 宣告內容必須與簽發權杖時一致（含代理繼承來的權限），
        /// 否則登入回傳的範圍會與之後每一次請求算出來的不同。
        /// </summary>
        private static ClaimsPrincipal PrincipalOf(int userId, IEnumerable<string> permissions) =>
            new(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    .. permissions.Select(p => new Claim("perm", p)),
                ],
                "login"));

        /// <summary>
        /// 開發站登入頁的「快速登入」清單。**正式站一律回空陣列。**
        ///
        /// ── ⚠⚠ 為什麼密碼不能寫在前端 ─────────────────────────────
        /// 原本這份清單（含 admin 與五個測試帳號的明文密碼）寫死在
        /// <c>ReactAdmin/src/pages/Login.tsx</c>。前端的 bundle 是**公開下載得到的**，
        /// 而開發站與正式站的測試帳號密碼**是同一組** ——
        /// ⇒ 只把按鈕從正式站藏起來**完全沒有用**：
        ///   任何人下載開發站的 js 就拿得到密碼，那組密碼在正式站照樣登得進去。
        /// ⭐ 判準：**能被下載的東西裡不可以有密碼。**「畫面上看不到」不等於「拿不到」。
        ///
        /// ── ⭐ 為什麼用設定檔而不是建置參數 ────────────────────────
        /// 建置參數（VITE_xxx）會讓兩站產出**兩份不同的檔案**，
        /// 那「開發站測過的那包」就不是「上正式站的那包」，測了也不算數。
        /// ⭐ 判準：**兩站跑同一份程式，差異放在設定檔，不要放在程式碼裡。**
        ///
        /// ── 兩道獨立的鎖，缺一不可 ────────────────────────────────
        /// 1. <b>環境</b>：Production 直接回空，**根本不去讀設定** ——
        ///    就算哪天有人把 stage 的設定檔誤複製到正式站，也不會外洩。
        /// 2. <b>設定</b>：沒有 <c>DemoAccounts</c> 這一段就回空 ——
        ///    正式站的 compose 本來就沒有這個環境變數。
        /// ⚠ 兩道鎖是**獨立**的，不是 else if —— 任何一道失守，另一道還在。
        /// </summary>
        [AllowAnonymous]
        [HttpGet("demo-accounts")]
        public IActionResult DemoAccounts()
        {
            // 第一道鎖：正式站到此為止，下面一行設定都不會讀
            if (env.IsProduction()) return Ok(Array.Empty<DemoAccount>());

            // 第二道鎖：設定檔／環境變數沒給就沒有
            var raw = config["DemoAccounts"];
            if (string.IsNullOrWhiteSpace(raw)) return Ok(Array.Empty<DemoAccount>());

            try
            {
                var list = JsonSerializer.Deserialize<List<DemoAccount>>(
                    raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return Ok(list ?? []);
            }
            catch (JsonException)
            {
                // ⚠ 設定寫壞了就當作「沒有」，不要讓整個登入頁掛掉 ——
                //   登入是所有人唯一的入口，它不能被一段選配設定拖累。
                return Ok(Array.Empty<DemoAccount>());
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            var user = await db.Users
                .Include(u => u.OrgUnit)
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role!)
                .ThenInclude(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
                .FirstOrDefaultAsync(u => u.Account == request.Account && u.IsActive);

            if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Unauthorized(new { message = "帳號或密碼錯誤", code = "invalid_credentials" });

            // 能否登入**只看「啟用」**（上面查詢的 IsActive），離職日不擋登入 ——
            // 2026-08-13 業主定案：人資確定離職時把「啟用」關掉即可；
            // 離職日只是人事資料（年資與請假期間計算用），設了離職日仍可登入。
            // 帳號管理列表會把「已設離職日」標示出來，讓「該關而未關」看得見。

            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var ownPermissions = user.UserRoles
                .SelectMany(ur => ur.Role!.RolePermissions)
                .Select(rp => rp.Permission!.Code)
                .Distinct()
                .ToList();

            // 代理期間內繼承被代理人的權限（IsDelegable=false 的敏感權限不繼承）
            var delegatedPermissions = await approvals.DelegatedPermissionCodesAsync(user.Id);
            var permissions = ownPermissions.Concat(delegatedPermissions).Distinct().ToList();

            var token = CreateToken(user.Id, user.Name, user.InstitutionId, permissions, user.TokenVersion);

            // ⭐ 每次登入都作廢既有的認證密碼解鎖 —— **重新進入敏感區域一律再驗一次**。
            // ⚠ 不可只靠前端登出時清掉憑證：憑證發出去之後，留著的人照樣能用。
            //    實測過，登出再登入時舊憑證仍然通、倒數還被刷新回 30 分。
            await stepUp.RevokeOnLoginAsync(user.Id);

            return Ok(new
            {
                token,
                mustChangePassword = user.MustChangePassword,
                user = new
                {
                    id = user.Id,
                    name = user.Name,
                    account = user.Account,
                    institutionId = user.InstitutionId,
                    orgUnitId = user.OrgUnitId,
                    orgUnitName = user.OrgUnit?.Name,
                    title = user.Title,
                    roles = user.UserRoles.Select(ur => ur.Role!.Name),
                    permissions,
                    delegatedPermissionCount = delegatedPermissions.Count,
                    scopes = await ScopesOfAsync(
                        PrincipalOf(user.Id, permissions), user.InstitutionId),
                },
            });
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await db.Users
                .Include(u => u.OrgUnit)
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return Unauthorized();

            return Ok(new
            {
                id = user.Id,
                name = user.Name,
                account = user.Account,
                institutionId = user.InstitutionId,
                orgUnitId = user.OrgUnitId,
                orgUnitName = user.OrgUnit?.Name,
                title = user.Title,
                mustChangePassword = user.MustChangePassword,
                roles = user.UserRoles.Select(ur => ur.Role!.Name),
                permissions = User.FindAll("perm").Select(c => c.Value),
                // 這裡用 User（權杖已帶著代理繼承後的權限），與登入時算出來的一致
                scopes = await ScopesOfAsync(User, user.InstitutionId),
            });
        }

        /// <summary>目前使用者可見的選單樹：依權限碼過濾；目錄無碼時看其下有無可見子項</summary>
        [Authorize]
        [HttpGet("menus")]
        public async Task<IActionResult> Menus()
        {
            var perms = User.FindAll("perm").Select(c => c.Value).ToHashSet();
            var isAdmin = perms.Contains("system.admin");

            var menus = await db.Menus.Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
                .ToListAsync();

            var tree = AdminMenusController.BuildTree(menus, null);
            return Ok(Filter(tree));

            List<MenuNode> Filter(List<MenuNode> nodes) => nodes
                .Select(n => n with { Children = Filter(n.Children) })
                .Where(n => n.PermissionCode is null
                    ? n.Children.Count > 0 || n.Path is not null
                    // ⭐ 「a|b」＝有其中一個就看得到（2026-09-30 帳戶收支明細：出納、會計、院長權限各不同）
                    : isAdmin || n.PermissionCode.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(perms.Contains))
                .ToList();
        }

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
                return BadRequest(new { message = "新密碼至少 8 碼", code = "weak_password" });

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await db.Users.FindAsync(userId);
            if (user is null) return Unauthorized();

            if (!BCrypt.Net.BCrypt.Verify(request.OldPassword, user.PasswordHash))
                return BadRequest(new { message = "舊密碼錯誤", code = "invalid_old_password" });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.MustChangePassword = false;
            await db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// 後台登入的權杖。
        /// ⚠⚠ 實際產生一律走 <see cref="TokenIssuer"/> —— **全系統只有那一份**。
        /// 這裡不再自己組 claim；理由見 TokenIssuer 檔頭。
        /// </summary>
        private string CreateToken(int userId, string name, int institutionId, List<string> permissions, int tokenVersion)
            => tokens.Create(userId, name, institutionId, permissions, tokenVersion: tokenVersion);
    }
}
