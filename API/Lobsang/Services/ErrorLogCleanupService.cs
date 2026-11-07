using Microsoft.EntityFrameworkCore;
using Lobsang.Data;

namespace Lobsang.Services
{
    /// <summary>
    /// 錯誤日誌自動清理：每日刪除超過保留期限的紀錄（預設 90 天）。
    /// 僅清理 error_logs —— audit_logs 是業務軌跡與評鑑佐證，不自動刪除。
    /// </summary>
    public class ErrorLogCleanupService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<ErrorLogCleanupService> logger) : BackgroundService
    {
        private const int DefaultRetentionDays = 90;

        private int RetentionDays =>
            int.TryParse(config["ErrorLog:RetentionDays"], out var d) && d > 0 ? d : DefaultRetentionDays;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 啟動後稍等，避開 migration 與 seeding
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                await CleanupAsync(stoppingToken);
                try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task CleanupAsync(CancellationToken ct)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // ExecuteDelete：直接下 DELETE，不載入實體
                var removed = await db.ErrorLogs
                    .Where(e => e.CreatedAt < cutoff)
                    .ExecuteDeleteAsync(ct);

                if (removed > 0)
                    logger.LogInformation("錯誤日誌清理：刪除 {Count} 筆超過 {Days} 天的紀錄", removed, RetentionDays);
            }
            catch (Exception ex)
            {
                // 清理失敗不影響服務運作，下個週期再試
                logger.LogError(ex, "錯誤日誌清理失敗");
            }
        }
    }
}
