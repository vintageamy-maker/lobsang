using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using Lobsang.Models;
using Lobsang.Services;
using System.Security.Claims;

namespace Lobsang.Controllers
{
    public record SaveDelegationRequest(int PrincipalUserId, int DelegateUserId, DateOnly StartDate, DateOnly EndDate, string? Reason, bool IsActive);

    /// <summary>代理人設定：代理期間內代理人可代簽（不繼承 IsDelegable=false 的敏感權限）</summary>
    [ApiController]
    [Route("api/admin/delegations")]
    [RequirePermission("delegation.manage")]
    public class AdminDelegationsController(AppDbContext db, AuditLogger audit) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] bool? activeOnly)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var query = db.UserDelegations
                .Include(d => d.Principal).Include(d => d.Delegate)
                .Where(d => d.InstitutionId == InstitutionId);

            if (activeOnly == true)
                query = query.Where(d => d.IsActive && d.StartDate <= today && d.EndDate >= today);

            var items = await query.OrderByDescending(d => d.StartDate).ThenByDescending(d => d.Id).ToListAsync();

            return Ok(items.Select(d => new
            {
                id = d.Id,
                principalUserId = d.PrincipalUserId,
                principalName = d.Principal!.Name,
                delegateUserId = d.DelegateUserId,
                delegateName = d.Delegate!.Name,
                startDate = d.StartDate,
                endDate = d.EndDate,
                reason = d.Reason,
                isActive = d.IsActive,
                // 是否正在生效中（供前端標示）
                inEffect = d.IsActive && d.StartDate <= today && d.EndDate >= today,
            }));
        }

        [HttpPost]
        public async Task<IActionResult> Create(SaveDelegationRequest request)
        {
            var error = await Validate(request, null);
            if (error is not null) return error;

            var delegation = new UserDelegation
            {
                InstitutionId = InstitutionId,
                PrincipalUserId = request.PrincipalUserId,
                DelegateUserId = request.DelegateUserId,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Reason = request.Reason,
                IsActive = request.IsActive,
            };
            db.UserDelegations.Add(delegation);
            audit.Log("delegation.create", "delegation", null, null, request);
            await db.SaveChangesAsync();
            return Ok(new { id = delegation.Id });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, SaveDelegationRequest request)
        {
            var delegation = await db.UserDelegations.FirstOrDefaultAsync(d => d.Id == id && d.InstitutionId == InstitutionId);
            if (delegation is null) return NotFound();

            var error = await Validate(request, id);
            if (error is not null) return error;

            var before = new { delegation.PrincipalUserId, delegation.DelegateUserId, delegation.StartDate, delegation.EndDate, delegation.IsActive };

            delegation.PrincipalUserId = request.PrincipalUserId;
            delegation.DelegateUserId = request.DelegateUserId;
            delegation.StartDate = request.StartDate;
            delegation.EndDate = request.EndDate;
            delegation.Reason = request.Reason;
            delegation.IsActive = request.IsActive;

            audit.Log("delegation.update", "delegation", id, before, request);
            await db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var delegation = await db.UserDelegations.FirstOrDefaultAsync(d => d.Id == id && d.InstitutionId == InstitutionId);
            if (delegation is null) return NotFound();

            audit.Log("delegation.delete", "delegation", id,
                new { delegation.PrincipalUserId, delegation.DelegateUserId, delegation.StartDate, delegation.EndDate }, null);
            db.UserDelegations.Remove(delegation);
            await db.SaveChangesAsync();
            return NoContent();
        }

        private async Task<IActionResult?> Validate(SaveDelegationRequest request, int? excludeId)
        {
            if (request.PrincipalUserId == request.DelegateUserId)
                return BadRequest(new { message = "代理人不可為本人", code = "self_delegation" });
            if (request.EndDate < request.StartDate)
                return BadRequest(new { message = "結束日不可早於開始日", code = "invalid_range" });

            var validUsers = await db.Users.CountAsync(u =>
                u.InstitutionId == InstitutionId && (u.Id == request.PrincipalUserId || u.Id == request.DelegateUserId));
            if (validUsers < 2)
                return BadRequest(new { message = "指定的使用者不存在", code = "invalid_user" });

            // 同一被代理人於同期間僅能有一位有效代理人（避免代簽權責不清）
            var overlap = await db.UserDelegations.AnyAsync(d =>
                d.InstitutionId == InstitutionId && d.IsActive && request.IsActive
                && d.PrincipalUserId == request.PrincipalUserId
                && (excludeId == null || d.Id != excludeId)
                && d.StartDate <= request.EndDate && d.EndDate >= request.StartDate);
            if (overlap)
                return BadRequest(new { message = "此期間已有其他有效代理設定", code = "overlap" });

            return null;
        }
    }
}
