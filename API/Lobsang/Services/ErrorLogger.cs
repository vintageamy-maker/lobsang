using Microsoft.AspNetCore.Http;
using Lobsang.Data;
using Lobsang.Models;
using System.Security.Claims;

namespace Lobsang.Services
{
    /// <summary>
    /// 錯誤日誌寫入。**刻意使用獨立的 DbContext scope**：
    /// 請求失敗時業務 DbContext 可能已處於錯誤狀態或交易將回滾，
    /// 共用同一個 context 會讓日誌一起消失（這正是 audit_logs 的限制）。
    /// </summary>
    public class ErrorLogger(IServiceScopeFactory scopeFactory, ILogger<ErrorLogger> logger)
    {
        public async Task LogAsync(HttpContext http, int statusCode,
            string? errorCode, string? message, Exception? exception = null)
        {
            try
            {
                var user = http.User;
                int? userId = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;
                int? institutionId = int.TryParse(user.FindFirstValue("inst"), out var inst) ? inst : null;

                var entry = new ErrorLog
                {
                    InstitutionId = institutionId,
                    UserId = userId,
                    UserName = Truncate(user.FindFirstValue(ClaimTypes.Name), 50),
                    Method = http.Request.Method,
                    Path = Truncate($"{http.Request.Path}{http.Request.QueryString}", 300),
                    StatusCode = statusCode,
                    ErrorCode = Truncate(errorCode, 50),
                    Message = Truncate(message, 500),
                    ExceptionType = Truncate(exception?.GetType().FullName, 200),
                    // 堆疊僅保留給伺服器端錯誤；4xx 是預期內的驗證結果
                    StackTrace = statusCode >= 500 ? Truncate(exception?.ToString(), 8000) : null,
                    TraceId = http.TraceIdentifier,
                    CreatedAt = DateTime.UtcNow,
                };

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.ErrorLogs.Add(entry);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // 日誌寫入失敗絕不可影響請求處理，退回到應用程式日誌
                logger.LogError(ex, "寫入錯誤日誌失敗（原始狀態碼 {StatusCode}）", statusCode);
            }
        }

        private static string? Truncate(string? value, int max) =>
            value is null ? null : value.Length <= max ? value : value[..max];
    }
}
