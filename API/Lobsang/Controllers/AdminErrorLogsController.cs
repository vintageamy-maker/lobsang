using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using System.Security.Claims;

namespace Lobsang.Controllers
{
    /// <summary>錯誤日誌查詢（唯讀）：失敗的請求留在這裡，成功的業務異動在「異動紀錄」</summary>
    [ApiController]
    [Route("api/admin/error-logs")]
    [RequirePermission("errorlog.read")]
    public class AdminErrorLogsController(AppDbContext db, IConfiguration config) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);

        private int RetentionDays =>
            int.TryParse(config["ErrorLog:RetentionDays"], out var d) && d > 0 ? d : 90;

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] int? statusCode, [FromQuery] string? keyword, [FromQuery] bool? serverErrorOnly,
            [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
            [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
        {
            // 未登入時發生的錯誤沒有機構歸屬，一併呈現以免漏掉登入階段的問題
            var query = db.ErrorLogs.Where(e => e.InstitutionId == InstitutionId || e.InstitutionId == null);

            if (statusCode.HasValue) query = query.Where(e => e.StatusCode == statusCode.Value);
            if (serverErrorOnly == true) query = query.Where(e => e.StatusCode >= 500);
            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(e => e.Path.Contains(keyword)
                                         || (e.Message != null && e.Message.Contains(keyword))
                                         || (e.ErrorCode != null && e.ErrorCode.Contains(keyword)));
            if (from.HasValue) query = query.Where(e => e.CreatedAt >= from.Value.ToDateTime(TimeOnly.MinValue));
            if (to.HasValue) query = query.Where(e => e.CreatedAt < to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));

            var total = await query.CountAsync();
            var items = await query.OrderByDescending(e => e.Id)
                .Skip((pageNumber - 1) * pageSize).Take(pageSize)
                .Select(e => new
                {
                    id = e.Id,
                    createdAt = e.CreatedAt,
                    userName = e.UserName,
                    method = e.Method,
                    path = e.Path,
                    statusCode = e.StatusCode,
                    errorCode = e.ErrorCode,
                    message = e.Message,
                    exceptionType = e.ExceptionType,
                    stackTrace = e.StackTrace,
                    traceId = e.TraceId,
                })
                .ToListAsync();

            return Ok(new PagedResult<object>(items, total));
        }

        /// <summary>近 7 日概況：供快速判斷是否有異常集中</summary>
        [HttpGet("summary")]
        public async Task<IActionResult> Summary()
        {
            var since = DateTime.UtcNow.AddDays(-7);
            var logs = db.ErrorLogs.Where(e =>
                (e.InstitutionId == InstitutionId || e.InstitutionId == null) && e.CreatedAt >= since);

            return Ok(new
            {
                total = await logs.CountAsync(),
                serverErrors = await logs.CountAsync(e => e.StatusCode >= 500),
                topPaths = await logs.GroupBy(e => e.Path)
                    .Select(g => new { path = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count).Take(5).ToListAsync(),
                retentionDays = RetentionDays,
                oldestAt = await db.ErrorLogs
                    .Where(e => e.InstitutionId == InstitutionId || e.InstitutionId == null)
                    .OrderBy(e => e.CreatedAt).Select(e => (DateTime?)e.CreatedAt).FirstOrDefaultAsync(),
            });
        }
    }
}
