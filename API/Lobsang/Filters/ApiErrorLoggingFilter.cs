using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Lobsang.Services;
using System.Text.Json;

namespace Lobsang.Filters
{
    /// <summary>
    /// 記錄 4xx 回應（驗證失敗、業務規則擋下、權限不足）。
    /// 例外（5xx）由 ExceptionHandlingMiddleware 處理，兩者互補。
    /// 401 不記錄 —— token 過期屬正常現象，記了只會洗版。
    /// </summary>
    public class ApiErrorLoggingFilter(ErrorLogger errorLogger) : IAsyncActionFilter
    {
        // 執行順序在 Program.cs 註冊時指定為 -3000：必須早於 [ApiController] 內建的
        // ModelStateInvalidFilter（Order = -2000），否則模型驗證失敗會在本過濾器執行前就短路。
        // 註：順序須在註冊處指定 —— Filters.Add<T>() 外層包了 TypeFilterAttribute，
        // 本類別自行實作 IOrderedFilter 會被忽略。

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context.Result is not null)
            {
                await LogIfClientError(context.HttpContext, context.Result);
                return;
            }

            // 後續過濾器（含模型驗證）若短路，其結果會回到這裡的 executed.Result
            var executed = await next();
            if (executed.Result is not null)
                await LogIfClientError(executed.HttpContext, executed.Result);
        }

        private async Task LogIfClientError(HttpContext http, IActionResult result)
        {
            var (status, value) = result switch
            {
                ObjectResult o => (o.StatusCode ?? 200, o.Value),
                StatusCodeResult s => (s.StatusCode, null),
                _ => (200, null),
            };

            if (status is < 400 or 401) return;

            var (code, message) = ExtractError(value);
            await errorLogger.LogAsync(http, status, code, message);
        }

        /// <summary>從回應物件取出 code / message（涵蓋自訂格式與 ProblemDetails）</summary>
        private static (string? Code, string? Message) ExtractError(object? value)
        {
            if (value is null) return (null, null);

            if (value is ValidationProblemDetails vpd)
            {
                var detail = string.Join("；", vpd.Errors
                    .SelectMany(e => e.Value.Select(m => $"{e.Key}: {m}")));
                return ("validation_error", detail);
            }
            if (value is ProblemDetails pd) return ("problem", pd.Detail ?? pd.Title);

            // 專案慣例：BadRequest(new { message, code })
            try
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
                var root = doc.RootElement;
                var code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
                var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                return (code, message);
            }
            catch
            {
                return (null, null);
            }
        }
    }
}
