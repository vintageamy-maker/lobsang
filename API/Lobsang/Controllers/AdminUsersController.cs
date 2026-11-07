using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using Lobsang.Models;
using Lobsang.Services;
using System.Security.Claims;
using System.Security.Cryptography;

namespace Lobsang.Controllers
{
    public record SaveUserRequest(
        string Account, string Name, string? Email, bool IsActive, List<int> RoleIds, string? Remark,
        int? OrgUnitId, string? EmployeeNo, string? Title, string? EmploymentType,
        DateOnly? HireDate = null,
        // 能否登入只看 IsActive，離職日不擋登入
        DateOnly? TerminationDate = null, string? TerminationReason = null,
        string? Phone = null);
    public record PagedResult<T>(IEnumerable<T> Items, int Total);
    /// <summary>解除 LINE 綁定。原因選填，但會進稽核軌跡</summary>
    public record UnbindLineRequest(string? Reason);

    /// <summary>
    /// 改派待簽關卡。<c>Items</c> 給空的就把**全部**改給 <c>ToUserId</c>。
    /// ⚠ 理由必填 —— 改派會改變「誰有權簽這張單」，那是稽核關心的事。
    /// </summary>
    public record ReassignApprovalsRequest(int? ToUserId, string Reason, List<ReassignItem>? Items);

    [ApiController]
    [Route("api/admin/users")]
    [RequirePermission("user.manage")]
    public class AdminUsersController(
        AppDbContext db, AuditLogger audit, UserDeactivationService deactivation) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);

        /// <summary>目前操作者（歷程留痕用）。取不到就記 null，不要因此讓整個異動失敗</summary>
        private int? ActorId =>
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? keyword, [FromQuery] int? roleId, [FromQuery] bool? isActive,
            [FromQuery] int? orgUnitId,
            [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var query = db.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .Include(u => u.OrgUnit)
                .Where(u => u.InstitutionId == InstitutionId);

            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(u => u.Account.Contains(keyword) || u.Name.Contains(keyword)
                                         || (u.EmployeeNo != null && u.EmployeeNo.Contains(keyword)));
            if (roleId.HasValue)
                query = query.Where(u => u.UserRoles.Any(ur => ur.RoleId == roleId.Value));
            if (isActive.HasValue)
                query = query.Where(u => u.IsActive == isActive.Value);
            if (orgUnitId.HasValue)
                query = query.Where(u => u.OrgUnitId == orgUnitId.Value);

            var total = await query.CountAsync();
            var users = await query.OrderBy(u => u.SortOrder).ThenBy(u => u.Id)
                .Skip((pageNumber - 1) * pageSize).Take(pageSize)
                .ToListAsync();

            return Ok(new PagedResult<object>(users.Select(ToDto), total));
        }

        private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        [HttpGet("role-options")]
        public async Task<IActionResult> RoleOptions() =>
            Ok(await db.Roles.Where(r => r.InstitutionId == InstitutionId)
                .OrderBy(r => r.Id)
                .Select(r => new { id = r.Id, name = r.Name })
                .ToListAsync());

        /// <summary>組織單位下拉選項（指派人員歸屬用）</summary>
        [HttpGet("org-unit-options")]
        public async Task<IActionResult> OrgUnitOptions() =>
            Ok(await db.OrgUnits.Where(o => o.InstitutionId == InstitutionId && o.IsActive)
                .OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
                .Select(o => new { id = o.Id, code = o.Code, name = o.Name, kind = o.Kind, parentId = o.ParentId })
                .ToListAsync());

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            var user = await FindUser(id);
            return user is null ? NotFound() : Ok(ToDto(user));
        }

        [HttpPost]
        public async Task<IActionResult> Create(SaveUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "帳號與姓名為必填", code = "required" });
            if (await db.Users.AnyAsync(u => u.Account == request.Account))
                return BadRequest(new { message = $"帳號「{request.Account}」已存在", code = "duplicate_account" });

            var initialPassword = GeneratePassword();
            var user = new User
            {
                InstitutionId = InstitutionId,
                // 新進人員排在最後
                SortOrder = (await db.Users.Where(u => u.InstitutionId == InstitutionId).MaxAsync(u => (int?)u.SortOrder) ?? 0) + 1,
                Account = request.Account.Trim(),
                Name = request.Name.Trim(),
                Email = request.Email,
                IsActive = request.IsActive,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(initialPassword),
                MustChangePassword = true,
                OrgUnitId = request.OrgUnitId,
                EmployeeNo = request.EmployeeNo,
                Title = request.Title,
                EmploymentType = request.EmploymentType,
                HireDate = request.HireDate,
                Phone = Nz(request.Phone),
                TerminationDate = request.TerminationDate,
                // 原因只在有離職日時有意義 —— 沒有日期的孤兒原因一律清掉
                TerminationReason = request.TerminationDate is null
                    ? null : NormalizeReason(request.TerminationReason),
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserRoles.AddRange(request.RoleIds.Distinct().Select(rid => new UserRole { UserId = user.Id, RoleId = rid }));
            audit.Log("user.create", "user", user.Id, null, new { user.Account, user.Name, user.Email, user.IsActive, roleIds = request.RoleIds });
            await db.SaveChangesAsync();

            var created = await FindUser(user.Id);
            // 初始密碼僅此一次回傳，不落庫明文
            return CreatedAtAction(nameof(Get), new { id = user.Id }, new { user = ToDto(created!), initialPassword });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, SaveUserRequest request)
        {
            var user = await FindUser(id);
            if (user is null) return NotFound();

            var before = new
            {
                user.Name, user.Email, user.IsActive, user.OrgUnitId, user.EmployeeNo, user.Title,
                user.EmploymentType, user.HireDate, user.TerminationDate, user.TerminationReason,
                user.Phone,
                roleIds = user.UserRoles.Select(ur => ur.RoleId).ToList(),
            };

            /*
             * ⚠⚠ 由「啟用」改成「停用」時，先擋。
             *
             * 送簽時簽核人就被寫死在單上（快照），所以人一停用，
             * 掛在他身上的單**不會自己脫困** —— 而且畫面上看不出異常，
             * 只是靜靜躺著沒人簽。等有人發現，通常已經拖很久了。
             *
             * ⭐ 這裡刻意做成**擋下**而不是提醒：可以按掉的提醒，
             *   跟現在什麼都不做的結果一樣。
             * ⚠ 四類都要清乾淨才放行 —— 只清「在途的單」的話，
             *   他在組織編制上還是主管，下一張新單子又會送回給他。
             */
            if (user.IsActive && !request.IsActive)
            {
                var impact = await deactivation.InspectAsync(InstitutionId, id);
                if (impact.Blocked)
                    return BadRequest(new
                    {
                        message = $"「{impact.Name}」還有 {impact.BlockingCount} 項簽核設定掛在身上，"
                                  + "請先處理完再停用。",
                        code = "deactivation_blocked",
                        impact,
                    });
            }

            user.Name = request.Name.Trim();
            user.Email = request.Email;
            user.IsActive = request.IsActive;
            user.OrgUnitId = request.OrgUnitId;
            user.EmployeeNo = request.EmployeeNo;
            user.Title = request.Title;
            user.EmploymentType = request.EmploymentType;
            user.HireDate = request.HireDate;
            user.Phone = Nz(request.Phone);
            user.TerminationDate = request.TerminationDate;
            user.TerminationReason = request.TerminationDate is null
                ? null : NormalizeReason(request.TerminationReason);
            db.UserRoles.RemoveRange(user.UserRoles);
            db.UserRoles.AddRange(request.RoleIds.Distinct().Select(rid => new UserRole { UserId = user.Id, RoleId = rid }));

            audit.Log("user.update", "user", user.Id, before, request);
            await db.SaveChangesAsync();

            var updated = await FindUser(id);
            return Ok(ToDto(updated!));
        }

        /// <summary>
        /// 停用這個帳號會影響到什麼（四類：在途待簽的關卡、單位主管身分、代理設定、
        /// 流程裡的指名關卡）。畫面在按下停用之前先問這一支。
        /// ⚠ 這是唯讀的，不會改任何東西。
        /// </summary>
        [HttpGet("{id:int}/deactivation-impact")]
        public async Task<IActionResult> DeactivationImpact(int id)
        {
            var user = await FindUser(id);
            if (user is null) return NotFound();
            return Ok(await deactivation.InspectAsync(InstitutionId, id));
        }

        /// <summary>
        /// 把指名給這個人的**待簽**關卡改派給別人。
        ///
        /// ⚠ 只動還沒簽的關卡；已經簽過的是既成事實。
        /// ⚠ 角色關卡（沒有指名到人的那種）不在此列 —— 那種關卡不會因為一個人停用而卡住。
        /// ⭐ 改派前後都寫進稽核：改派會改變「誰有權簽這張單」。
        /// </summary>
        [HttpPost("{id:int}/reassign-approvals")]
        public async Task<IActionResult> ReassignApprovals(int id, ReassignApprovalsRequest request)
        {
            var user = await FindUser(id);
            if (user is null) return NotFound();

            if (string.IsNullOrWhiteSpace(request.Reason))
                return BadRequest(new { message = "請填寫改派原因", code = "reason_required" });

            List<(string DocType, int StepId, string From, string To)> done;
            try
            {
                done = await deactivation.ReassignAsync(
                    InstitutionId, id, request.ToUserId, request.Items);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message, code = "reassign_rejected" });
            }

            if (done.Count == 0)
                return Ok(new { changed = 0, message = "沒有需要改派的關卡" });

            audit.Log("approval.reassign", "user", id,
                new { from = user.Name, steps = done.Select(d => new { d.DocType, d.StepId }) },
                new { to = done[0].To, reason = request.Reason.Trim(), count = done.Count });
            await db.SaveChangesAsync();

            return Ok(new
            {
                changed = done.Count,
                items = done.Select(d => new
                {
                    docType = d.DocType,
                    docTypeLabel = ApprovalDocType.Label(d.DocType),
                    stepId = d.StepId, from = d.From, to = d.To,
                }),
            });
        }

        /// <summary>重設密碼：產生隨機密碼一次性回傳，並強制下次登入改密</summary>
        [HttpPost("{id:int}/reset-password")]
        public async Task<IActionResult> ResetPassword(int id)
        {
            var user = await FindUser(id);
            if (user is null) return NotFound();

            var newPassword = GeneratePassword();
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.MustChangePassword = true;

            audit.Log("user.reset_password", "user", user.Id, null, new { user.Account });
            await db.SaveChangesAsync();

            return Ok(new { newPassword });
        }

        /// <summary>
        /// 管理員解除某人的 LINE 綁定。
        ///
        /// ⚠ 這條路是**必要的補救手段**，不只是方便：
        /// <list type="bullet">
        /// <item>手機遺失／換 LINE 帳號 —— 本人碰不到原本那個 LINE，自助解綁沒用</item>
        /// <item>綁錯人 —— 知道他人密碼者可把自己的 LINE 綁到別人帳號，
        ///   且**改密碼也不會讓那個綁定失效**，只有這裡能救</item>
        /// <item>離職 —— 停用只擋功能，綁定關係仍在，要一併清掉</item>
        /// </list>
        /// </summary>
        [HttpPost("{id:int}/unbind-line")]
        public async Task<IActionResult> UnbindLine(int id, [FromBody] UnbindLineRequest? request)
        {
            var user = await FindUser(id);
            if (user is null) return NotFound();
            if (user.LineUserId is null) return BadRequest(new { message = "這個帳號沒有綁定 LINE" });

            var lineUserId = user.LineUserId;
            user.LineUserId = null;
            user.LineBoundAt = null;

            db.LineBindLogs.Add(new Models.LineBindLog
            {
                InstitutionId = user.InstitutionId,
                UserId = user.Id,
                LineUserId = lineUserId,
                Action = Models.LineBindAction.Unbind,
                // ⚠ 存 UTC（前端會加 'Z' 換算）。不可用 UtcNow.AddHours(8)，
                //    那是打卡時刻的慣例，用在這裡畫面會多 8 小時。
                //    原寫 DateTime.Now，容器是 UTC 所以碰巧對，改成明寫。
                ActedAt = DateTime.UtcNow,
                // ⚠ 執行者是管理員不是本人 —— 這一欄要分得出來，
                //    否則查不出「這個綁定是誰解的」
                ActedBy = ActorId,
                ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Reason = string.IsNullOrWhiteSpace(request?.Reason) ? "後台解除" : request!.Reason,
            });

            audit.Log("user.unbind_line", "user", user.Id, new { lineBound = true },
                      new { lineBound = false, reason = request?.Reason });
            await db.SaveChangesAsync();
            return Ok(new { message = "已解除綁定" });
        }

        private async Task<User?> FindUser(int id) => await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.OrgUnit)
            .FirstOrDefaultAsync(u => u.Id == id && u.InstitutionId == InstitutionId);

        private static object ToDto(User u) => new
        {
            id = u.Id,
            account = u.Account,
            name = u.Name,
            email = u.Email,
            isActive = u.IsActive,
            mustChangePassword = u.MustChangePassword,
            lastLoginAt = u.LastLoginAt,
            roleIds = u.UserRoles.Select(ur => ur.RoleId),
            roles = u.UserRoles.Select(ur => ur.Role!.Name),
            createdAt = u.CreatedAt,
            orgUnitId = u.OrgUnitId,
            orgUnitName = u.OrgUnit?.Name,
            employeeNo = u.EmployeeNo,
            title = u.Title,
            employmentType = u.EmploymentType,
            hireDate = u.HireDate,
            terminationDate = u.TerminationDate,
            terminationReason = u.TerminationReason,
            phone = u.Phone,
            // ⚠ 只回「有沒有綁」與時間，**不回 LineUserId 本身** ——
            //    那是可以用來對應到個人 LINE 帳號的識別碼，列表頁不需要它。
            lineBound = u.LineUserId != null,
            lineBoundAt = u.LineBoundAt,
        };

        private static string? NormalizeReason(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>12 碼隨機密碼（含大小寫/數字/符號各至少一）</summary>
        private static string GeneratePassword()
        {
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string lower = "abcdefghijkmnpqrstuvwxyz";
            const string digits = "23456789";
            const string symbols = "!@#$%^&*";
            const string all = upper + lower + digits + symbols;

            var chars = new List<char>
            {
                upper[RandomNumberGenerator.GetInt32(upper.Length)],
                lower[RandomNumberGenerator.GetInt32(lower.Length)],
                digits[RandomNumberGenerator.GetInt32(digits.Length)],
                symbols[RandomNumberGenerator.GetInt32(symbols.Length)],
            };
            while (chars.Count < 12)
                chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);

            return new string(chars.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToArray());
        }
    }
}
