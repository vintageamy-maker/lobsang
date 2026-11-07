using Lobsang.Data;
using Lobsang.Models;
using System.Security.Claims;
using System.Text.Json;

namespace Lobsang.Services
{
    /// <summary>
    /// 異動紀錄：呼叫 Log() 後由呼叫端的 SaveChangesAsync 一併寫入（與主要操作同交易）。
    /// </summary>
    public class AuditLogger(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public void Log(string action, string targetType, int? targetId, object? before, object? after)
        {
            var user = httpContextAccessor.HttpContext?.User;
            int? userId = int.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;
            int institutionId = int.TryParse(user?.FindFirstValue("inst"), out var inst) ? inst : 0;

            db.AuditLogs.Add(new AuditLog
            {
                InstitutionId = institutionId,
                UserId = userId,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                BeforeJson = before is null ? null : JsonSerializer.Serialize(before, JsonOpts),
                AfterJson = after is null ? null : JsonSerializer.Serialize(after, JsonOpts),
                CreatedAt = DateTime.UtcNow,
            });
        }
    }
}
