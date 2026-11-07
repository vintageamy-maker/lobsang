using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using Lobsang.Models;
using Lobsang.Services;

namespace Lobsang.Controllers
{
    /// <summary>待辦的嚴重度。urgent 會在畫面上加底色</summary>
    public enum TodoSeverity { Info, Warn, Urgent }

    /// <summary>
    /// 首頁的一則待辦。
    /// <see cref="Route"/> ＋ <see cref="Tab"/> 是落點 —— 由**後端**決定要把人帶到哪，
    /// 前端不自己拼；同一個待辦日後改掛別的頁面時只有這裡要改。
    /// </summary>
    public record TodoItem(
        string Key, string Group, string Title, string Detail,
        int Count, string Route, string? Tab, TodoSeverity Severity);

    /// <summary>
    /// 登入後的首頁待辦（聚合端點）。
    ///
    /// ⚠ <b>為什麼是一支聚合端點，而不是讓前端打很多支。</b>
    /// 前端逐支呼叫的話，沒有該權限的人會拿到一串 403。
    /// **看不看得到某一則待辦，是後端的決定。**
    ///
    /// ⚠ 每一項的判準都沿用該模組自己的那一套，不另寫近似版：
    /// 簽核類走 <see cref="ApprovalResolver.PendingStep"/> ＋ <c>CanAct</c>（與各模組的待簽清單同源）。
    /// 另寫一份「大概是這樣」的判準，結果就是首頁說有 3 件、點進去只有 1 件。
    ///
    /// ⭐ 新專案加了自己的單據（請假、請款…），在這裡各補一段 Add…Async。
    /// </summary>
    [ApiController]
    [Route("api/admin/dashboard")]
    [Authorize]
    public class AdminDashboardController(AppDbContext db, ApprovalResolver approvals) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);
        private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private bool IsAdmin => User.HasPermission("system.admin");

        [HttpGet("todo")]
        public async Task<IActionResult> Todo()
        {
            var items = new List<TodoItem>();
            await AddApprovalsAsync(items);
            await AddMineAsync(items);

            return Ok(new
            {
                items = items
                    .Where(i => i.Count > 0)
                    .OrderByDescending(i => i.Severity)
                    .ThenBy(i => i.Group)
                    .Select(i => new
                    {
                        i.Key, i.Group, i.Title, i.Detail, i.Count, i.Route, i.Tab,
                        severity = i.Severity.ToString().ToLowerInvariant(),
                    }),
            });
        }

        private async Task AddApprovalsAsync(List<TodoItem> items)
        {
            if (!User.HasPermission("record.read")) return;
            var ctx = await approvals.BuildContextAsync(UserId, IsAdmin);

            var rows = await db.WorkRecords
                .Include(r => r.ApprovalSteps)
                .Where(r => r.InstitutionId == InstitutionId && r.Status == RecordStatus.InReview)
                .ToListAsync();
            var n = rows.Count(r => r.CurrentStep is not null
                                    && ApprovalResolver.PendingStep(r.ApprovalSteps, r.CurrentStep)
                                        is { } s && approvals.CanAct(s, ctx).CanAct);
            items.Add(new TodoItem("record-approve", "簽核",
                "表單待你簽核", "同意或退件；退件請填原因，表單會退回填寫人的草稿。",
                n, "/records", "pending", TodoSeverity.Urgent));
        }

        private async Task AddMineAsync(List<TodoItem> items)
        {
            // ⚠ 退件是把單子退回 Draft，所以「被退回」與「自己存的草稿」
            //    光看狀態分不出來，必須看關卡有沒有 Rejected。
            // ⚠ 工作記錄沒有 UserId —— 填寫人是 CreatedBy
            var recordBack = await db.WorkRecords
                .Include(r => r.ApprovalSteps)
                .Where(r => r.CreatedBy == UserId && r.Status == RecordStatus.Draft)
                .ToListAsync();
            items.Add(new TodoItem("record-returned", "我的",
                "表單被退回", "退件會回到草稿，修改後可重新送簽。退件原因在明細裡。",
                recordBack.Count(r => WasReturned(r.ApprovalSteps)), "/records", "mine", TodoSeverity.Warn));
        }

        private static bool WasReturned<TStep>(List<TStep> steps) where TStep : IApprovalStep =>
            steps.Count > 0
            && steps.Where(s => s.Round == steps.Max(x => x.Round))
                .Any(s => s.Status == ApprovalStepStatus.Rejected);
    }
}
