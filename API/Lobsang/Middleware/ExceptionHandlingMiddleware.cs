using Lobsang.Services;
using System.Text.Json;

namespace Lobsang.Middleware
{
    /// <summary>
    /// 全域例外處理：攔截未處理例外 → 寫入錯誤日誌 → 回傳統一格式。
    /// 不把例外細節回給前端（避免洩漏內部結構），但回傳 traceId 供比對日誌。
    /// </summary>
    public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        public async Task InvokeAsync(HttpContext context, ErrorLogger errorLogger)
        {
            try
            {
                await next(context);
            }
            // ⭐ 使用者送進來的內容不合法 ⇒ 400 並如實說明原因，不是 500。
            //   ⚠ 這類例外**不寫錯誤日誌** —— 它不是系統故障，而是「使用者填錯了」。
            //     混進錯誤日誌會讓真正的故障被淹沒。
            catch (UserInputException ex)
            {
                if (context.Response.HasStarted) return;
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(
                    new { message = ex.Message, code = ex.Code },
                    new JsonSerializerOptions
                    {
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    }));
            }
            // 兩個人同時簽同一關（會簽）⇒ 409，請後到的人重新整理再按（不是系統故障，不寫錯誤日誌）
            catch (ApprovalConflictException ex)
            {
                if (context.Response.HasStarted) return;
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(
                    new { message = ex.Message, code = "approval_conflict" },
                    new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "未處理例外 {Method} {Path}", context.Request.Method, context.Request.Path);
                await errorLogger.LogAsync(context, StatusCodes.Status500InternalServerError,
                    "unhandled_exception", ex.Message, ex);

                if (context.Response.HasStarted)
                {
                    // 回應已開始輸出就無法改寫，僅留日誌
                    return;
                }

                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    message = "伺服器發生錯誤，請稍後再試或聯繫系統管理員",
                    code = "internal_error",
                    traceId = context.TraceIdentifier,
                }, new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }));
            }
        }
    }
}
