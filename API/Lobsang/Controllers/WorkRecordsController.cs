using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using Lobsang.Models;
using Lobsang.Services;
using System.Security.Claims;

namespace Lobsang.Controllers
{
    public record SaveWorkRecordRequest(
        int FormDefinitionId, DateOnly RecordDate, string ContentJson, List<string> Tags);

    public record ApprovalActionRequest(string? Comment);
    public record BatchApproveRequest(List<int> Ids, string? Comment);

    [ApiController]
    [Route("api/admin/form-definitions")]
    public class FormDefinitionsController(AppDbContext db) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);

        /// <summary>
        [HttpGet]
        [RequirePermission("record.read")]
        public async Task<IActionResult> List()
        {
            var defs = await db.FormDefinitions
                .Where(f => f.InstitutionId == InstitutionId && f.IsActive)
                .OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
                .ToListAsync();
            // ⚠ 請假／補卡／加班的定義只承載簽核設定，不是可以填的表 ——
            //   它們有專屬頁面與型別化資料表，從「新增記錄」開一張會繞過那些業務規則，
            //   而那三件事都會動到個人的考勤。⇒ 不列進來。
            defs = defs.Where(f => !f.IsApprovalOnly).ToList();
            return Ok(defs.Select(f => new
            {
                id = f.Id, code = f.Code, name = f.Name,
                category = f.Category, frequency = f.Frequency.ToString(),
            }));
        }

        [HttpGet("{id:int}")]
        [RequirePermission("record.read")]
        public async Task<IActionResult> Get(int id)
        {
            var f = await db.FormDefinitions
                .FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == InstitutionId);
            if (f is null) return NotFound();

            // ⚠ 關卡現在掛在流程底下，不再直接掛表單。這一支是唯讀的預覽，
            //    有多條流程時把它們攤平列出（帶上是哪一段），不做條件判斷。
            var steps = await db.FormApprovalSteps
                .Include(s => s.Role)
                .Include(s => s.ApprovalFlow)
                .Where(s => s.ApprovalFlow!.FormDefinitionId == id && s.ApprovalFlow.IsActive)
                .OrderBy(s => s.ApprovalFlow!.SortOrder).ThenBy(s => s.StepOrder)
                .Select(s => new
                {
                    flowId = s.ApprovalFlowId,
                    flowName = s.ApprovalFlow!.Name,
                    conditionKind = s.ApprovalFlow.ConditionKind.ToString(),
                    s.ApprovalFlow.MinValue,
                    s.ApprovalFlow.MaxValue,
                    s.StepOrder,
                    stepType = s.StepType.ToString(),
                    roleName = s.Role != null ? s.Role.Name : null,
                    s.ManagerLevel,
                })
                .ToListAsync();

            return Ok(new
            {
                id = f.Id, code = f.Code, name = f.Name,
                category = f.Category, frequency = f.Frequency.ToString(),
                schemaJson = f.SchemaJson,
                approvalSteps = steps,
            });
        }
    }

    [ApiController]
    [Route("api/admin/work-records")]
    public class WorkRecordsController(AppDbContext db, ApprovalResolver approvals) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);
        private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private bool IsAdmin => User.FindAll("perm").Any(c => c.Value == "system.admin");

        // ===== 查詢 =====

        [HttpGet]
        [RequirePermission("record.read")]
        public async Task<IActionResult> List(
            [FromQuery] int? formDefinitionId, [FromQuery] string? status,
            [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
            [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var query = await VisibleAsync(BaseQuery());

            if (formDefinitionId.HasValue) query = query.Where(r => r.FormDefinitionId == formDefinitionId.Value);
            if (Enum.TryParse<RecordStatus>(status, true, out var s)) query = query.Where(r => r.Status == s);
            if (from.HasValue) query = query.Where(r => r.RecordDate >= from.Value);
            if (to.HasValue) query = query.Where(r => r.RecordDate <= to.Value);

            return Ok(await ToPagedDto(query, pageNumber, pageSize));
        }

        /// <summary>待我簽核：簽核中且目前關卡指派到我（本人／我的角色／我代理的對象）</summary>
        [HttpGet("pending-approvals")]
        [RequirePermission("record.read")]
        public async Task<IActionResult> PendingApprovals(
            [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var ctx = await approvals.BuildContextAsync(UserId, IsAdmin);
            var roleIds = ctx.EffectiveRoleIds;
            var assignedIds = ctx.PrincipalIds.Concat([UserId]).ToList();

            // ⚠ 判準必須與**核可動作**那一端一致（Approve 走 ApprovalResolver.CanAct）。
            //    CanAct 對 system.admin 是全面放行，而這裡的條件原本沒有 ——
            //    結果是管理者簽得下去、卻在待簽清單裡看不到那一筆。
            //    首頁待辦（同樣走 CanAct）出現後這個落差就變成
            //    「首頁說 2 件、點進去 1 件」，正是待辦要避免的事。
            //    ⚠ 保留 SQL 端過濾而不是撈回記憶體再篩：工作記錄是每日產生的，
            //    量級與請假／加班不同，這一頁有分頁就是為了這個。
            /* ⭐ 會簽／任一位（2026-10-09）：名單在 JSON 裡，SQL 篩不到 ⇒
             *   先把「目前關卡是會簽、而且我還能簽」的單號撈出來（量很小），再併進下面的條件。 */
            var coSignSteps = await db.WorkRecordApprovalSteps
                .Where(a => a.CoSignJson != null && a.Status == ApprovalStepStatus.Pending
                            && a.WorkRecord!.Status == RecordStatus.InReview
                            && a.StepOrder == a.WorkRecord.CurrentStep)
                .ToListAsync();
            var coSignIds = coSignSteps.Where(a => approvals.CanAct(a, ctx) is (true, _, false))
                .Select(a => a.WorkRecordId).Distinct().ToList();

            var query = (await VisibleAsync(BaseQuery())).Where(r => r.Status == RecordStatus.InReview &&
                (IsAdmin || coSignIds.Contains(r.Id) || r.ApprovalSteps.Any(a =>
                    a.StepOrder == r.CurrentStep &&
                    a.Status == ApprovalStepStatus.Pending &&
                    ((a.AssignedUserId != null && assignedIds.Contains(a.AssignedUserId.Value))
                     || (a.RoleId != null && roleIds.Contains(a.RoleId.Value))))));

            return Ok(await ToPagedDto(query, pageNumber, pageSize));
        }

        /// <summary>
        [HttpGet("{id:int}")]
        [RequirePermission("record.read")]
        public async Task<IActionResult> Get(int id)
        {
            var r = await db.WorkRecords
                .Include(x => x.FormDefinition)
                .Include(x => x.Tags)
                .Include(x => x.ApprovalSteps)
                .FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == InstitutionId);
            if (r is null) return NotFound();
            // ⚠ 看不到的一律當作不存在（不回 403，免得透露「有這一筆」）
            if (!await CanSeeAsync(id)) return NotFound();

            var ctx = await approvals.BuildContextAsync(UserId, IsAdmin);
            var pendingStep = CurrentPendingStep(r);
            // ⚠ 第三個值（是不是管理員代簽）工作記錄**尚未處理** ——
            //   業主 2026-09-14 指定先做補卡／請假／加班三種；這裡先丟棄，行為不變。
            var (canApprove, onBehalfOf, _) = pendingStep is null
                ? (false, (int?)null, false)
                : approvals.CanAct(pendingStep, ctx);

            string? onBehalfOfName = null;
            if (onBehalfOf.HasValue)
                onBehalfOfName = await db.Users.Where(u => u.Id == onBehalfOf.Value).Select(u => u.Name).FirstOrDefaultAsync();

            // 列印頁要的：機構名稱（表頭）
            var institutionName = await db.Institutions.Where(i => i.Id == InstitutionId).Select(i => i.Name).FirstOrDefaultAsync();

            return Ok(new
            {
                id = r.Id,
                formDefinitionId = r.FormDefinitionId,
                formName = r.FormDefinition!.Name,
                formCode = r.FormDefinition!.Code,
                institutionName,
                createdByName = await db.Users.Where(u => u.Id == r.CreatedBy).Select(u => u.Name).FirstOrDefaultAsync(),
                recordDate = r.RecordDate,
                createdAt = r.CreatedAt,
                status = r.Status.ToString(),
                contentJson = r.ContentJson,
                tags = r.Tags.Select(x => x.Tag),
                submittedAt = r.SubmittedAt,
                currentStep = r.CurrentStep,
                canApprove,
                approveOnBehalfOfName = onBehalfOfName,
                canWithdraw = CanWithdraw(r),
                isOwner = r.CreatedBy == UserId,
                wasRejected = WasRejected(r),
                cancelReason = r.CancelReason,
                approvalSteps = r.ApprovalSteps
                    .OrderBy(a => a.Round).ThenBy(a => a.StepOrder)
                    .Select(a => new
                    {
                        round = a.Round, stepOrder = a.StepOrder,
                        name = a.DisplayName,
                        isNamed = a.AssignedUserId != null,
                        status = a.Status.ToString(),
                        actedByName = a.ActedByName,
                        onBehalfOfName = a.OnBehalfOfName,
                        actedAt = a.ActedAt, comment = a.Comment,
                    }),
            });
        }

        // ===== 草稿 =====

        [HttpPost]
        [RequirePermission("record.write")]
        public async Task<IActionResult> Create(SaveWorkRecordRequest request)
        {
            var def = await db.FormDefinitions
                .FirstOrDefaultAsync(f => f.Id == request.FormDefinitionId
                    && f.InstitutionId == InstitutionId && f.IsActive);
            if (def is null) return BadRequest(new { message = "表單定義不存在或已停用", code = "invalid_form" });

            // ⚠⚠ 只從畫面上拿掉是不夠的 —— 直接打 API 一樣建得出來。
            //    請假／補卡／加班有專屬頁面與型別化資料表，從這裡開會繞過那些業務規則，
            //    而那三件事都會動到個人的考勤。
            if (def.IsApprovalOnly)
                return BadRequest(new
                {
                    message = $"「{def.Name}」請從專屬頁面申請，不從工作記錄新增",
                    code = "approval_only_form",
                });

            var record = new WorkRecord
            {
                InstitutionId = InstitutionId,
                FormDefinitionId = request.FormDefinitionId,
                RecordDate = request.RecordDate,
                ContentJson = string.IsNullOrWhiteSpace(request.ContentJson) ? "{}" : request.ContentJson,
                Status = RecordStatus.Draft,
            };
            db.WorkRecords.Add(record);
            await db.SaveChangesAsync();

            SyncLinks(record, request);
            await db.SaveChangesAsync();
            return CreatedAtAction(nameof(Get), new { id = record.Id }, new { id = record.Id });
        }

        [HttpPut("{id:int}")]
        [RequirePermission("record.write")]
        public async Task<IActionResult> Update(int id, SaveWorkRecordRequest request)
        {
            var record = await FindRecord(id);
            if (record is null) return NotFound();
            if (record.Status != RecordStatus.Draft)
                return BadRequest(new { message = "簽核中、已完成或已撤銷的記錄不可修改", code = "record_locked" });

            record.RecordDate = request.RecordDate;
            record.ContentJson = string.IsNullOrWhiteSpace(request.ContentJson) ? "{}" : request.ContentJson;
            SyncLinks(record, request);
            await db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        [RequirePermission("record.write")]
        public async Task<IActionResult> Delete(int id)
        {
            var record = await FindRecord(id);
            if (record is null) return NotFound();
            if (record.Status != RecordStatus.Draft)
                return BadRequest(new { message = "僅草稿可刪除；已送簽的記錄請改用抽單或撤銷", code = "record_locked" });

            record.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return NoContent();
        }

        // ===== 簽核流程 =====

        /// <summary>送簽：解析關卡設定產生本輪快照；無關卡設定則直接完成</summary>
        [HttpPost("{id:int}/submit")]
        [RequirePermission("record.write")]
        public async Task<IActionResult> Submit(int id)
        {
            var record = await FindRecord(id);
            if (record is null) return NotFound();
            if (record.Status != RecordStatus.Draft)
                return BadRequest(new { message = "僅草稿可送簽", code = "invalid_status" });

            var round = (record.ApprovalSteps.Count == 0 ? 0 : record.ApprovalSteps.Max(a => a.Round)) + 1;
            var submitterId = record.CreatedBy ?? UserId;
            /* ⭐ 條件分流：簽核流程可依「金額／天數／等級」選不同路線（見 ApprovalFlow.ConditionKind）。
             *   新專案的表單若有可比的數字（例如請款金額），在這裡從 ContentJson 取出放進 ApprovalFacts；
             *   沒有就傳 null，走「不分條件」的那條流程。 */
            ApprovalFacts? facts = null;
            var steps = await approvals.ResolveStepsAsync(record.FormDefinitionId, record.Id, round, submitterId, null, facts);

            /*
             * ⚠⚠ SubmittedAt ＝ **送簽時間**，兩條路都要寫。
             *
             * 只寫在其中一條的話，在途的單「什麼時候送的」查不出來，也沒有起算點可以做逾期提醒。
             * ⭐ 完成時間不必另存 —— 最後一關的 ActedAt 就是。
             */
            record.SubmittedAt = DateTime.UtcNow;

            if (steps.Count == 0)
            {
                record.Status = RecordStatus.Submitted;
                await db.SaveChangesAsync();
                return Ok(new { status = record.Status.ToString() });
            }

            db.WorkRecordApprovalSteps.AddRange(steps);
            record.Status = RecordStatus.InReview;
            record.CurrentStep = steps.First().StepOrder;
            await db.SaveChangesAsync();

            return Ok(new
            {
                status = record.Status.ToString(),
                currentStep = record.CurrentStep,
                currentStepName = steps.First().DisplayName,
            });
        }

        /// <summary>同意：關卡通過並推進；最後一關通過＝完成</summary>
        [HttpPost("{id:int}/approve")]
        [RequirePermission("record.read")]
        public async Task<IActionResult> Approve(int id, ApprovalActionRequest request)
        {
            var (record, step, onBehalfOf, error) = await LoadActionableStep(id);
            if (error is not null) return error;

            // ⭐ 會簽（2026-10-09）：還有人沒簽 ⇒ 只記下這一位，不往下一關
            if (!await approvals.CoSignActAsync(step!, UserId, onBehalfOf, ApprovalStepStatus.Approved,
                    string.IsNullOrWhiteSpace(request.Comment) ? "同意" : request.Comment.Trim()))
            {
                await db.SaveChangesAsync();
                return Ok(new { status = record!.Status.ToString(), currentStep = record.CurrentStep, coSignPending = true });
            }
            await ActOnStep(step!, ApprovalStepStatus.Approved,
                string.IsNullOrWhiteSpace(request.Comment) ? "同意" : request.Comment.Trim(), onBehalfOf);
            AdvanceOrComplete(record!, step!);

            await db.SaveChangesAsync();
            return Ok(new { status = record!.Status.ToString(), currentStep = record.CurrentStep });
        }

        /// <summary>退件：意見必填；記錄回到草稿由填寫者修改後重送</summary>
        [HttpPost("{id:int}/reject")]
        [RequirePermission("record.read")]
        public async Task<IActionResult> Reject(int id, ApprovalActionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Comment))
                return BadRequest(new { message = "退件必須填寫意見", code = "comment_required" });

            var (record, step, onBehalfOf, error) = await LoadActionableStep(id);
            if (error is not null) return error;

            await approvals.CoSignActAsync(step!, UserId, onBehalfOf, ApprovalStepStatus.Rejected, request.Comment.Trim());

            await ActOnStep(step!, ApprovalStepStatus.Rejected, request.Comment.Trim(), onBehalfOf);
            record!.Status = RecordStatus.Draft;
            record.CurrentStep = null;
            await db.SaveChangesAsync();
            return Ok(new { status = record.Status.ToString() });
        }

        /// <summary>抽單：送簽後尚無人簽核前，填寫者可收回改回草稿</summary>
        [HttpPost("{id:int}/withdraw")]
        [RequirePermission("record.write")]
        public async Task<IActionResult> Withdraw(int id)
        {
            var record = await FindRecord(id);
            if (record is null) return NotFound();
            if (record.Status != RecordStatus.InReview)
                return BadRequest(new { message = "僅簽核中的記錄可抽單", code = "invalid_status" });
            if (!CanWithdraw(record))
                return BadRequest(new { message = "已有關卡完成簽核，無法抽單；請洽簽核者退件", code = "already_approved" });
            if (record.CreatedBy != UserId && !IsAdmin)
                return StatusCode(403, new { message = "僅填寫者可抽回此記錄", code = "not_owner" });

            // 本輪關卡作廢：移除未簽關卡，回到草稿（歷史輪次保留）
            var latestRound = record.ApprovalSteps.Max(a => a.Round);
            db.WorkRecordApprovalSteps.RemoveRange(record.ApprovalSteps.Where(a => a.Round == latestRound));
            record.Status = RecordStatus.Draft;
            record.CurrentStep = null;
            await db.SaveChangesAsync();
            return Ok(new { status = record.Status.ToString() });
        }

        /// <summary>撤銷：已完成的記錄撤回（主管職權；資料保留、必填原因）</summary>
        [HttpPost("{id:int}/cancel")]
        [RequirePermission("record.cancel")]
        public async Task<IActionResult> Cancel(int id, ApprovalActionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Comment))
                return BadRequest(new { message = "撤銷必須填寫原因", code = "comment_required" });

            var record = await FindRecord(id);
            if (record is null) return NotFound();
            if (record.Status != RecordStatus.Submitted)
                return BadRequest(new { message = "僅已完成的記錄可撤銷", code = "invalid_status" });

            record.Status = RecordStatus.Cancelled;
            record.CancelledAt = DateTime.UtcNow;
            record.CancelledBy = UserId;
            record.CancelReason = request.Comment.Trim();
            await db.SaveChangesAsync();
            return Ok(new { status = record.Status.ToString() });
        }

        /// <summary>批次同意（供組長/督導一次簽多筆）；逐筆處理並回報結果</summary>
        [HttpPost("approve-batch")]
        [RequirePermission("record.read")]
        public async Task<IActionResult> ApproveBatch(BatchApproveRequest request)
        {
            var results = new List<object>();
            foreach (var id in request.Ids.Distinct())
            {
                var (record, step, onBehalfOf, error) = await LoadActionableStep(id);
                if (error is not null)
                {
                    results.Add(new { id, ok = false });
                    continue;
                }
                if (await approvals.CoSignActAsync(step!, UserId, onBehalfOf, ApprovalStepStatus.Approved,
                        string.IsNullOrWhiteSpace(request.Comment) ? "同意" : request.Comment.Trim()))
                {
                    await ActOnStep(step!, ApprovalStepStatus.Approved,
                        string.IsNullOrWhiteSpace(request.Comment) ? "同意" : request.Comment.Trim(), onBehalfOf);
                    AdvanceOrComplete(record!, step!);
                }

                await db.SaveChangesAsync();
                results.Add(new { id, ok = true });
            }
            return Ok(new { results });
        }

        // ===== 私有工具 =====

        private IQueryable<WorkRecord> BaseQuery() => db.WorkRecords
            .Include(r => r.FormDefinition)
            .Include(r => r.ApprovalSteps)
            .Where(r => r.InstitutionId == InstitutionId);

        private async Task<object> ToPagedDto(IQueryable<WorkRecord> query, int pageNumber, int pageSize)
        {
            var total = await query.CountAsync();
            var records = await query
                .OrderByDescending(r => r.RecordDate).ThenByDescending(r => r.Id)
                .Skip((pageNumber - 1) * pageSize).Take(pageSize)
                .ToListAsync();

            var userIds = records.Where(r => r.CreatedBy.HasValue).Select(r => r.CreatedBy!.Value).Distinct().ToList();
            var userNames = await db.Users.Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name);

            return new PagedResult<object>(records.Select(r => new
            {
                id = r.Id,
                formDefinitionId = r.FormDefinitionId,
                formName = r.FormDefinition!.Name,
                recordDate = r.RecordDate,
                status = r.Status.ToString(),
                currentStepName = r.Status == RecordStatus.InReview ? CurrentPendingStep(r)?.DisplayName : null,
                wasRejected = WasRejected(r),
                createdBy = r.CreatedBy.HasValue ? userNames.GetValueOrDefault(r.CreatedBy.Value) : null,
                isOwner = r.CreatedBy == UserId,
                updatedAt = r.UpdatedAt,
                submittedAt = r.SubmittedAt,
            }), total);
        }

        // 判定收斂到 ApprovalResolver.PendingStep（帶 currentStep＝精確定位到該關卡）
        private static WorkRecordApprovalStep? CurrentPendingStep(WorkRecord r) =>
            r.CurrentStep is null ? null : ApprovalResolver.PendingStep(r.ApprovalSteps, r.CurrentStep);

        /// <summary>草稿狀態且最新一輪含退件＝被退件（前端顯示提示）</summary>
        private static bool WasRejected(WorkRecord r)
        {
            if (r.Status != RecordStatus.Draft || r.ApprovalSteps.Count == 0) return false;
            var latestRound = r.ApprovalSteps.Max(a => a.Round);
            return r.ApprovalSteps.Any(a => a.Round == latestRound && a.Status == ApprovalStepStatus.Rejected);
        }

        /// <summary>簽核中且本輪尚無任何關卡完成 → 可抽單</summary>
        private static bool CanWithdraw(WorkRecord r)
        {
            if (r.Status != RecordStatus.InReview || r.ApprovalSteps.Count == 0) return false;
            var latestRound = r.ApprovalSteps.Max(a => a.Round);
            return !r.ApprovalSteps.Any(a => a.Round == latestRound && a.Status != ApprovalStepStatus.Pending);
        }

        private static void AdvanceOrComplete(WorkRecord record, WorkRecordApprovalStep step)
        {
            var nextStep = record.ApprovalSteps
                .Where(a => a.Round == step.Round && a.StepOrder > step.StepOrder)
                .OrderBy(a => a.StepOrder)
                .FirstOrDefault();

            if (nextStep is null)
            {
                record.Status = RecordStatus.Submitted;
                // ⚠ 這裡**不可以**再寫 SubmittedAt —— 那會把送簽時間蓋成完成時間。
                //    完成時間看最後一關的 ActedAt。
                record.CurrentStep = null;
            }
            else record.CurrentStep = nextStep.StepOrder;
        }

        private async Task<(WorkRecord? record, WorkRecordApprovalStep? step, int? onBehalfOf, IActionResult? error)> LoadActionableStep(int id)
        {
            var record = await FindRecord(id);
            if (record is null) return (null, null, null, NotFound());
            if (record.Status != RecordStatus.InReview)
                return (null, null, null, BadRequest(new { message = "此記錄不在簽核中", code = "invalid_status" }));

            var step = CurrentPendingStep(record);
            if (step is null)
                return (null, null, null, BadRequest(new { message = "簽核狀態異常，請重新整理", code = "step_not_found" }));

            var ctx = await approvals.BuildContextAsync(UserId, IsAdmin);
            // ⚠ 管理員代簽的標示工作記錄尚未處理（見本檔上方註解）
            var (canAct, onBehalfOf, _) = approvals.CanAct(step, ctx);
            if (!canAct)
                return (null, null, null, StatusCode(403,
                    new { message = $"目前關卡為「{step.DisplayName}」，您沒有此關的簽核權", code = "not_your_turn" }));

            return (record, step, onBehalfOf, null);
        }

        private async Task ActOnStep(WorkRecordApprovalStep step, ApprovalStepStatus status, string comment, int? onBehalfOf)
        {
            var actorName = await db.Users.Where(u => u.Id == UserId).Select(u => u.Name).FirstOrDefaultAsync();
            step.Status = status;
            step.ActedBy = UserId;
            step.ActedByName = actorName;
            step.ActedAt = DateTime.UtcNow;
            step.Comment = comment;

            if (onBehalfOf.HasValue)
            {
                step.OnBehalfOfUserId = onBehalfOf;
                step.OnBehalfOfName = await db.Users.Where(u => u.Id == onBehalfOf.Value)
                    .Select(u => u.Name).FirstOrDefaultAsync();
            }
        }

        private async Task<WorkRecord?> FindRecord(int id) => !await CanSeeAsync(id) ? null : await db.WorkRecords
            .Include(r => r.Tags).Include(r => r.ApprovalSteps)
            .FirstOrDefaultAsync(r => r.Id == id && r.InstitutionId == InstitutionId);

        /// <summary>
        /// 記錄的可見範圍。預設：record.read 的人全部看得到。
        /// ⭐ 新專案有「保密表單」（例如申訴）時，把表單代碼放進 <see cref="ConfidentialCodes"/>：
        ///   只有建立者、簽核關卡上的人（含代理）看得到，其他人連「有這一筆」都不會知道。
        /// </summary>
        private static readonly string[] ConfidentialCodes = [];

        private async Task<IQueryable<WorkRecord>> VisibleAsync(IQueryable<WorkRecord> q)
        {
            if (ConfidentialCodes.Length == 0 || IsAdmin) return q;

            var hidden = ConfidentialCodes;
            var ctx = await approvals.BuildContextAsync(UserId, IsAdmin);
            var roleIds = ctx.EffectiveRoleIds;
            var assignedIds = ctx.PrincipalIds.Concat([UserId]).ToList();
            var me = UserId;
            return q.Where(r => !hidden.Contains(r.FormDefinition!.Code)
                || r.CreatedBy == me
                || r.ApprovalSteps.Any(a => (a.AssignedUserId != null && assignedIds.Contains(a.AssignedUserId.Value))
                                          || (a.RoleId != null && roleIds.Contains(a.RoleId.Value))));
        }

        private async Task<bool> CanSeeAsync(int id) =>
            await (await VisibleAsync(db.WorkRecords.Where(r => r.Id == id && r.InstitutionId == InstitutionId))).AnyAsync();

        private void SyncLinks(WorkRecord record, SaveWorkRecordRequest request)
        {
            db.WorkRecordTags.RemoveRange(record.Tags);
            db.WorkRecordTags.AddRange(request.Tags
                .Select(t => t.Trim()).Where(t => t.Length > 0).Distinct()
                .Select(t => new WorkRecordTag { WorkRecordId = record.Id, Tag = t }));
        }
    }
}
