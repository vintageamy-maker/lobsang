using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using System.Security.Claims;

namespace Lobsang.Controllers
{
    /// <summary>異動紀錄查詢（唯讀）：誰、何時、對哪筆資料做了什麼、改前改後</summary>
    [ApiController]
    [Route("api/admin/audit-logs")]
    [RequirePermission("audit.read")]
    public class AdminAuditLogsController(AppDbContext db) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? action, [FromQuery] string? targetType, [FromQuery] int? userId,
            [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
            [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
        {
            var query = db.AuditLogs.Where(a => a.InstitutionId == InstitutionId);

            if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action.Contains(action));
            if (!string.IsNullOrWhiteSpace(targetType)) query = query.Where(a => a.TargetType == targetType);
            if (userId.HasValue) query = query.Where(a => a.UserId == userId.Value);
            if (from.HasValue) query = query.Where(a => a.CreatedAt >= from.Value.ToDateTime(TimeOnly.MinValue));
            if (to.HasValue) query = query.Where(a => a.CreatedAt < to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));

            var total = await query.CountAsync();
            var logs = await query.OrderByDescending(a => a.Id)
                .Skip((pageNumber - 1) * pageSize).Take(pageSize)
                .ToListAsync();

            var userIds = logs.Where(l => l.UserId.HasValue).Select(l => l.UserId!.Value).Distinct().ToList();
            var userNames = await db.Users.Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name);

            return Ok(new PagedResult<object>(logs.Select(l => new
            {
                id = l.Id,
                action = l.Action,
                targetType = l.TargetType,
                targetId = l.TargetId,
                userId = l.UserId,
                userName = l.UserId.HasValue ? userNames.GetValueOrDefault(l.UserId.Value) : null,
                beforeJson = l.BeforeJson,
                afterJson = l.AfterJson,
                createdAt = l.CreatedAt,
            }), total));
        }

        /// <summary>篩選用：目前有紀錄的動作與目標類型</summary>
        [HttpGet("filter-options")]
        public async Task<IActionResult> FilterOptions()
        {
            var actions = await db.AuditLogs.Where(a => a.InstitutionId == InstitutionId)
                .Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
            var targetTypes = await db.AuditLogs.Where(a => a.InstitutionId == InstitutionId)
                .Select(a => a.TargetType).Distinct().OrderBy(t => t).ToListAsync();
            return Ok(new { actions, targetTypes });
        }
    }
}
