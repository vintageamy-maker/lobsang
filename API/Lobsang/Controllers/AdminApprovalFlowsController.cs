using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using Lobsang.Models;
using Lobsang.Services;
using System.Security.Claims;

namespace Lobsang.Controllers
{
    /// <summary>
    /// ⚠⚠ <c>StepType</c> 刻意做成**可為 null**。
    ///
    /// 它若是非 null 的列舉，呼叫端漏傳時 model binding 會安靜地給預設值
    /// （<c>Role</c>，因為它是列舉的第 0 項），於是「漏傳」與「真的要 Role」
    /// 在後端**分不出來** —— 2026-09-13 就是這樣：前端把調班單的「對調的另一方」
    /// 那一關整關漏掉，後端把它當成沒選角色的 Role，回報的卻是
    /// 「調班一定要有『對調的另一方』這一關」，訊息與真正的原因對不起來。
    /// ⇒ 做成 null 才能明講「這一關沒有指定類型」。
    /// </summary>
    public record SaveFlowStepRequest(
        int StepOrder, ApprovalStepType? StepType,
        int? RoleId, int? ManagerLevel, int? SpecificUserId,
        /// <summary>簽核方式（2026-10-09）：預設／任一位／會簽；舊畫面沒送就是預設</summary>
        ApprovalSignMode SignMode = ApprovalSignMode.Default);

    /// <summary>在某個數值切一刀，把「以上不限」那一段拆成兩段</summary>
    public record SplitFlowRequest(
        decimal AtValue, ApprovalConditionKind ConditionKind,
        string? LowerName, string? UpperName);

    public record SaveApprovalFlowRequest(
        int FormDefinitionId, ApprovalConditionKind ConditionKind,
        decimal MinValue, decimal? MaxValue,
        string? Name, bool IsActive, int SortOrder,
        List<SaveFlowStepRequest> Steps);

    /// <summary>
    /// 簽核流程設定：一張表單有哪幾條流程（依條件分流）、每條流程有哪幾關。
    ///
    /// ⚠⚠ 這一頁改的是**誰有權簽誰的單**，敏感度等同權限設定 ——
    ///   故獨立為 <c>approval.config</c> 權限，不與一般設定混用。
    ///
    /// ⭐ 改流程**不影響在途單據**：送簽當下就把關卡解析成快照寫在單上。
    ///   這是刻意的 —— 組織或流程異動不該回頭改變已送出的單，否則稽核解釋不了。
    /// </summary>
    [ApiController]
    [Route("api/admin/approval-flows")]
    [RequirePermission("approval.config")]
    public class AdminApprovalFlowsController(AppDbContext db, AuditLogger audit) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);

        /// <summary>有簽核流程可設定的表單。⚠ 含請假／補卡／加班那三張「只承載流程」的定義</summary>
        [HttpGet("forms")]
        public async Task<IActionResult> Forms()
        {
            var forms = await db.FormDefinitions
                .Where(f => f.InstitutionId == InstitutionId && f.IsActive)
                .OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
                .Select(f => new
                {
                    f.Id, f.Code, f.Name, f.Category,
                    FlowCount = db.ApprovalFlows.Count(x => x.FormDefinitionId == f.Id),
                })
                .ToListAsync();
            return Ok(forms.Select(f => new
            {
                id = f.Id, code = f.Code, name = f.Name, category = f.Category, flowCount = f.FlowCount,
                // ⭐ 讓畫面知道要不要顯示分流相關的東西（切成兩段、條件欄位、指路說明）
                // ⚠ 用 ApprovalConditionSupport 的那一份清單，**不要再抄一份**
                supportsCondition = ApprovalConditionSupport.Supports(f.Code),
                // ⭐ 這張表單可以依哪幾種數字分流（畫面只列這幾種）
                supportedKinds = ApprovalConditionSupport.KindsFor(f.Code).Select(k => k.ToString()),
            }));
        }

        /// <summary>某張表單的所有流程與關卡</summary>
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int formDefinitionId)
        {
            var flows = await db.ApprovalFlows
                .Include(f => f.Steps).ThenInclude(s => s.Role)
                .Where(f => f.InstitutionId == InstitutionId && f.FormDefinitionId == formDefinitionId)
                .OrderBy(f => f.SortOrder).ThenBy(f => f.MinValue)
                .ToListAsync();

            var userIds = flows.SelectMany(f => f.Steps)
                .Where(s => s.SpecificUserId.HasValue)
                .Select(s => s.SpecificUserId!.Value).Distinct().ToList();
            var names = await db.Users.Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name);

            return Ok(flows.Select(f => new
            {
                id = f.Id,
                conditionKind = f.ConditionKind.ToString(),
                minValue = f.MinValue,
                maxValue = f.MaxValue,
                name = f.Name,
                isActive = f.IsActive,
                sortOrder = f.SortOrder,
                steps = f.Steps.OrderBy(s => s.StepOrder).Select(s => new
                {
                    id = s.Id,
                    stepOrder = s.StepOrder,
                    stepType = s.StepType.ToString(),
                    roleId = s.RoleId,
                    roleName = s.Role?.Name,
                    managerLevel = s.ManagerLevel,
                    specificUserId = s.SpecificUserId,
                    specificUserName = s.SpecificUserId is int u ? names.GetValueOrDefault(u) : null,
                    signMode = s.SignMode.ToString(),
                }),
            }));
        }

        /// <summary>
        /// 關卡可以指派給誰。
        /// ⚠⚠ 人員清單**只列啟用中的** —— 讓停用的人出現在下拉裡，
        ///   只會造出「選了之後單子永遠卡住」的設定。
        /// </summary>
        [HttpGet("options")]
        public async Task<IActionResult> Options() => Ok(new
        {
            roles = await db.Roles
                .Where(r => r.InstitutionId == InstitutionId)
                .OrderBy(r => r.Id)
                .Select(r => new { id = r.Id, name = r.Name })
                .ToListAsync(),
            users = await db.Users
                .Where(u => u.InstitutionId == InstitutionId && u.IsActive)
                .OrderBy(u => u.SortOrder).ThenBy(u => u.Id)
                .Select(u => new { id = u.Id, name = u.Name, title = u.Title })
                .ToListAsync(),
        });

        [HttpPost]
        public async Task<IActionResult> Create(SaveApprovalFlowRequest r)
        {
            if (await ValidateAsync(r, null) is IActionResult bad) return bad;

            var flow = new ApprovalFlow { InstitutionId = InstitutionId, FormDefinitionId = r.FormDefinitionId };
            Apply(flow, r);
            db.ApprovalFlows.Add(flow);
            await db.SaveChangesAsync();

            ReplaceSteps(flow.Id, r);
            audit.Log("approval_flow.create", "approval_flow", flow.Id, null,
                new { r.FormDefinitionId, r.ConditionKind, r.MinValue, r.MaxValue, stepCount = r.Steps.Count });
            await db.SaveChangesAsync();
            return Ok(new { id = flow.Id });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, SaveApprovalFlowRequest r)
        {
            var flow = await db.ApprovalFlows.Include(f => f.Steps)
                .FirstOrDefaultAsync(f => f.Id == id && f.InstitutionId == InstitutionId);
            if (flow is null) return NotFound();
            if (await ValidateAsync(r, id) is IActionResult bad) return bad;

            var before = new
            {
                flow.ConditionKind, flow.MinValue, flow.MaxValue, flow.Name, flow.IsActive,
                steps = flow.Steps.OrderBy(s => s.StepOrder)
                    .Select(s => new { s.StepOrder, s.StepType, s.RoleId, s.ManagerLevel, s.SpecificUserId, s.SignMode })
                    .ToList(),
            };

            Apply(flow, r);
            // ⭐ 關卡整組換掉。⚠ 這不影響任何在途單據 —— 單據上是快照，不指向這裡。
            db.FormApprovalSteps.RemoveRange(flow.Steps);
            ReplaceSteps(flow.Id, r);

            audit.Log("approval_flow.update", "approval_flow", flow.Id, before, r);
            await db.SaveChangesAsync();
            return Ok(new { id = flow.Id });
        }

        /// <summary>
        /// ⭐⭐ **把一段切成兩段** —— 這是建立級距的正路。
        ///
        /// ── 為什麼要有這支（2026-08-23 實測發現的死結）──────────────
        /// 只用新增／編輯做不到「從單一無條件流程變成兩段級距」：
        ///   - 想加第二段 → 被擋，因為第一段還是「無條件」
        ///   - 想把第一段改成「未滿三天」→ 被擋，因為最後一段必須是「以上不限」
        /// 兩邊互相卡死，使用者**永遠設不出級距**。
        ///
        /// ⭐ 切割的心智模型才是對的：級距不是各自獨立的區間，而是**數線上的分界點**。
        ///   「三天以上要多一關」＝ 在 3 這裡切一刀。
        ///   這樣做，級距**在建構上就不可能有縫或重疊**。
        ///
        /// 新的那一段沿用原本的關卡（通常只是要在後面多加一關），使用者再去編輯。
        /// </summary>
        [HttpPost("{id:int}/split")]
        public async Task<IActionResult> Split(int id, SplitFlowRequest r)
        {
            var flow = await db.ApprovalFlows.Include(f => f.Steps)
                .FirstOrDefaultAsync(f => f.Id == id && f.InstitutionId == InstitutionId);
            if (flow is null) return NotFound();

            if (flow.MaxValue is not null)
                return BadRequest(new
                {
                    message = "只能切割最後一段（以上不限的那一段）。",
                    code = "not_last_tier",
                });

            if (r.AtValue <= flow.MinValue)
                return BadRequest(new
                {
                    message = $"切割點要大於 {flow.MinValue}。",
                    code = "bad_split_point",
                });

            if (r.ConditionKind == ApprovalConditionKind.None)
                return BadRequest(new { message = "要先選依什麼分流（天數／時數／金額／事件等級）。", code = "kind_required" });

            var code = await db.FormDefinitions
                .Where(f => f.Id == flow.FormDefinitionId).Select(f => f.Code).FirstOrDefaultAsync();
            if (!ApprovalConditionSupport.Supports(code, r.ConditionKind))
            {
                var name = await db.FormDefinitions
                    .Where(f => f.Id == flow.FormDefinitionId).Select(f => f.Name).FirstOrDefaultAsync();
                return BadRequest(new
                {
                    message = ApprovalConditionSupport.Reason(name),
                    code = "condition_not_supported",
                });
            }

            var others = await db.ApprovalFlows
                .Where(f => f.Id != id && f.FormDefinitionId == flow.FormDefinitionId
                            && f.ConditionKind != ApprovalConditionKind.None
                            && f.ConditionKind != r.ConditionKind)
                .AnyAsync();
            if (others)
                return BadRequest(new { message = "這張表單的其他流程用的是別種條件，不能混用。", code = "kind_mixed" });

            // 原本那段收尾到切割點；新的一段從切割點起、以上不限
            var upper = new ApprovalFlow
            {
                InstitutionId = InstitutionId,
                FormDefinitionId = flow.FormDefinitionId,
                ConditionKind = r.ConditionKind,
                MinValue = r.AtValue,
                MaxValue = null,
                Name = r.UpperName,
                IsActive = true,
                SortOrder = flow.SortOrder + 1,
            };
            db.ApprovalFlows.Add(upper);
            await db.SaveChangesAsync();

            foreach (var s in flow.Steps.OrderBy(s => s.StepOrder))
                db.FormApprovalSteps.Add(new FormApprovalStep
                {
                    ApprovalFlowId = upper.Id, StepOrder = s.StepOrder, StepType = s.StepType,
                    RoleId = s.RoleId, ManagerLevel = s.ManagerLevel, SpecificUserId = s.SpecificUserId, SignMode = s.SignMode,
                });

            flow.ConditionKind = r.ConditionKind;
            flow.MaxValue = r.AtValue;
            flow.Name = r.LowerName ?? flow.Name;

            audit.Log("approval_flow.split", "approval_flow", flow.Id,
                new { flow.MinValue, MaxValue = (decimal?)null },
                new { at = r.AtValue, r.ConditionKind, newFlowId = upper.Id });
            await db.SaveChangesAsync();

            return Ok(new { lowerId = flow.Id, upperId = upper.Id });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var flow = await db.ApprovalFlows
                .FirstOrDefaultAsync(f => f.Id == id && f.InstitutionId == InstitutionId);
            if (flow is null) return NotFound();

            var siblings = await db.ApprovalFlows
                .Where(f => f.Id != id && f.FormDefinitionId == flow.FormDefinitionId && f.IsActive)
                .Select(f => new { f.ConditionKind, f.MinValue, f.MaxValue })
                .ToListAsync();

            // ⚠⚠ 刪光了會讓這張表單**完全沒有簽核流程** —— 送簽會直接變成「已完成」。
            //    那是很難察覺的行為改變（單子送出去就過了），明確擋下。
            if (siblings.Count == 0)
                return BadRequest(new
                {
                    message = "這是最後一條流程，刪掉之後這張表單送簽會直接完成、沒有人簽核。"
                              + "若要停用簽核，請改成「停用」而不是刪除。",
                    code = "last_flow",
                });

            /*
             * ⭐⭐ 刪一段 ＝ **把分界點拿掉**，不是在數線上挖一個洞。
             *
             * 所以要把相鄰那段接起來，級距才會保持完整：
             *   刪中間或最後一段 → 前一段的上限延伸過來（最後一段就變成「以上不限」）
             *   刪第一段         → 後一段的下限退回這一段的下限（通常是 0）
             *
             * ⚠ 沒有這個接合，使用者刪掉一段之後，落在那個區間的單子會找不到流程 ——
             *   而且要等到有人真的請那個天數才會爆。
             */
            if (flow.ConditionKind != ApprovalConditionKind.None)
            {
                var peers = await db.ApprovalFlows
                    .Where(f => f.Id != id && f.FormDefinitionId == flow.FormDefinitionId
                                && f.ConditionKind != ApprovalConditionKind.None)
                    .OrderBy(f => f.MinValue)
                    .ToListAsync();

                var prev = peers.LastOrDefault(f => f.MinValue < flow.MinValue);
                if (prev is not null) prev.MaxValue = flow.MaxValue;
                else
                {
                    var next = peers.FirstOrDefault(f => f.MinValue >= flow.MinValue);
                    if (next is not null) next.MinValue = flow.MinValue;
                }

                // 只剩一段時退回「無條件」—— 一段的級距等於沒有分流，留著條件只會讓人困惑
                if (peers.Count == 1)
                {
                    peers[0].ConditionKind = ApprovalConditionKind.None;
                    peers[0].MinValue = 0;
                    peers[0].MaxValue = null;
                    peers[0].Name = "預設流程";
                }
            }

            db.ApprovalFlows.Remove(flow);
            audit.Log("approval_flow.delete", "approval_flow", flow.Id,
                new { flow.ConditionKind, flow.MinValue, flow.MaxValue }, null);
            await db.SaveChangesAsync();
            return NoContent();
        }

        // ─────────────────────────────────────────────────────────────

        private static void Apply(ApprovalFlow flow, SaveApprovalFlowRequest r)
        {
            flow.ConditionKind = r.ConditionKind;
            flow.MinValue = r.ConditionKind == ApprovalConditionKind.None ? 0 : r.MinValue;
            flow.MaxValue = r.ConditionKind == ApprovalConditionKind.None ? null : r.MaxValue;
            flow.Name = string.IsNullOrWhiteSpace(r.Name) ? null : r.Name.Trim();
            flow.IsActive = r.IsActive;
            flow.SortOrder = r.SortOrder;
        }

        private void ReplaceSteps(int flowId, SaveApprovalFlowRequest r)
        {
            var order = 1;
            foreach (var s in r.Steps.OrderBy(s => s.StepOrder))
                db.FormApprovalSteps.Add(new FormApprovalStep
                {
                    ApprovalFlowId = flowId,
                    // ⚠ 序號一律重編 —— 前端刪掉中間一關之後若沿用原序號會出現 1、3，
                    //    而唯一索引是 (flow, step_order)，看起來沒事但排序與顯示都會怪。
                    StepOrder = order++,
                    // ⚠ null 在 ValidateAsync 已經先擋掉了（step_type_missing），走到這裡必定有值
                    StepType = s.StepType!.Value,
                    RoleId = s.StepType == ApprovalStepType.SpecificUser ? null : s.RoleId,
                    ManagerLevel = s.StepType == ApprovalStepType.ManagerChain
                        ? Math.Max(1, s.ManagerLevel ?? 1) : null,
                    SpecificUserId = s.StepType == ApprovalStepType.SpecificUser ? s.SpecificUserId : null,
                    // ⭐ 簽核方式只對主管、角色關卡有意義；其他關卡一律存預設
                    SignMode = s.StepType is ApprovalStepType.ManagerChain or ApprovalStepType.Role ? s.SignMode : ApprovalSignMode.Default,
                });
        }

        /// <summary>回傳非 null 代表擋下</summary>
        private async Task<IActionResult?> ValidateAsync(SaveApprovalFlowRequest r, int? selfId)
        {
            if (r.Steps.Count == 0)
                return BadRequest(new { message = "至少要有一個簽核關卡。", code = "no_step" });

            // ⚠⚠ 先擋「沒有指定類型」，才不會被下面的規則冒名頂替成別的原因。
            var noType = r.Steps.OrderBy(s => s.StepOrder)
                .Select((s, i) => (s, i)).FirstOrDefault(x => x.s.StepType is null);
            if (noType.s is not null)
                return BadRequest(new
                {
                    message = $"第 {noType.i + 1} 關沒有指定關卡類型（送出的內容裡少了這一關的資料）。",
                    code = "step_type_missing",
                });

            /*
             * ⚠⚠ 「對調的另一方」不是可以設定掉的東西。
             *
             * 把它拿掉 ＝ **不經當事人同意就能換他的班**。那是合意，不是流程偏好，
             * 不該由設定畫面決定有沒有。
             * ⇒ 調班單必須有且只有一關，而且要在第一關；其他表單一關都不能有
             *   （沒有對造的單據，引擎解析時會把它整關略過，等於默默少一關）。
             *
             * ⭐ 畫面上也會鎖住那一關不讓改，但把關要在這裡 ——
             *   只擋畫面的話，就是「畫面在演系統會檢核」。
             */
            var formCode = await db.FormDefinitions
                .Where(f => f.Id == r.FormDefinitionId && f.InstitutionId == InstitutionId)
                .Select(f => f.Code).FirstOrDefaultAsync();
            var consent = r.Steps.Where(s => s.StepType == ApprovalStepType.Counterpart).ToList();

            if (formCode == "shift-swap")
            {
                if (consent.Count == 0)
                    return BadRequest(new
                    {
                        message = "調班一定要有「對調的另一方」這一關 —— "
                                  + "少了它就變成不經當事人同意也能換他的班。",
                        code = "consent_required",
                    });
                if (consent.Count > 1)
                    return BadRequest(new { message = "「對調的另一方」只能有一關。", code = "consent_duplicated" });
                if (r.Steps.OrderBy(s => s.StepOrder).First().StepType != ApprovalStepType.Counterpart)
                    return BadRequest(new
                    {
                        message = "「對調的另一方」必須是第一關 —— "
                                  + "當事人都還沒同意，不該先送去給人核准。",
                        code = "consent_not_first",
                    });
            }
            else if (consent.Count > 0)
            {
                return BadRequest(new
                {
                    message = "只有調班單有「對調的另一方」。其他單據沒有對造，"
                              + "設了會被引擎略過，等於默默少一關。",
                    code = "consent_not_applicable",
                });
            }

            foreach (var s in r.Steps)
            {
                if (s.StepType == ApprovalStepType.Role && s.RoleId is null)
                    return BadRequest(new { message = $"第 {s.StepOrder} 關選了「指定角色」但沒有選角色。", code = "step_misconfigured" });
                if (s.StepType == ApprovalStepType.SpecificUser && s.SpecificUserId is null)
                    return BadRequest(new { message = $"第 {s.StepOrder} 關選了「指定人員」但沒有選人。", code = "step_misconfigured" });
            }

            // ⚠⚠ 指定人員必須是啟用中的 —— 指到停用的人，那一關會永遠沒人簽。
            var picked = r.Steps.Where(s => s.SpecificUserId.HasValue)
                .Select(s => s.SpecificUserId!.Value).Distinct().ToList();
            if (picked.Count > 0)
            {
                var inactive = await db.Users
                    .Where(u => picked.Contains(u.Id) && (!u.IsActive || u.InstitutionId != InstitutionId))
                    .Select(u => u.Name).ToListAsync();
                if (inactive.Count > 0)
                    return BadRequest(new
                    {
                        message = $"「{string.Join("、", inactive)}」已停用，不能指定為簽核人。",
                        code = "inactive_approver",
                    });
            }

            // ── 級距完整性：把這次要存的那一條套進去，再看整組對不對 ──
            var others = await db.ApprovalFlows
                .Where(f => f.FormDefinitionId == r.FormDefinitionId
                            && f.InstitutionId == InstitutionId
                            && f.IsActive && (selfId == null || f.Id != selfId))
                .Select(f => new { f.ConditionKind, f.MinValue, f.MaxValue })
                .ToListAsync();

            // ⚠⚠ 沒有數字可比的表單不給設條件（理由見 ApprovalConditionSupport）
            if (r.ConditionKind != ApprovalConditionKind.None
                && !ApprovalConditionSupport.Supports(formCode, r.ConditionKind))
            {
                var formName = await db.FormDefinitions
                    .Where(f => f.Id == r.FormDefinitionId).Select(f => f.Name).FirstOrDefaultAsync();
                return BadRequest(new
                {
                    message = ApprovalConditionSupport.Reason(formName),
                    code = "condition_not_supported",
                });
            }

            if (r.ConditionKind == ApprovalConditionKind.None)
            {
                if (others.Any(f => f.ConditionKind != ApprovalConditionKind.None))
                    return BadRequest(new
                    {
                        message = "這張表單已經有依條件分流的流程，不能再加一條「無條件」的。",
                        code = "kind_mixed",
                    });
                if (r.IsActive && others.Count > 0)
                    return BadRequest(new
                    {
                        message = "「無條件」的流程只能有一條。要依情況分流請改成天數／時數／金額。",
                        code = "duplicate_none",
                    });
                return null;
            }

            // ⚠ 同一張表單的級距必須是同一種 kind ——「三天以內」和「一萬以下」比不出來
            var conflicting = others.FirstOrDefault(
                f => f.ConditionKind != ApprovalConditionKind.None && f.ConditionKind != r.ConditionKind);
            if (conflicting is not null)
                return BadRequest(new
                {
                    message = $"這張表單的其他流程是依「{KindLabel(conflicting.ConditionKind)}」分流，"
                              + $"不能混用「{KindLabel(r.ConditionKind)}」。",
                    code = "kind_mixed",
                });

            if (others.Any(f => f.ConditionKind == ApprovalConditionKind.None))
                return BadRequest(new
                {
                    message = "這張表單目前是「無條件」的單一流程。要改成分流，請先把那一條也設定條件。",
                    code = "kind_mixed",
                });

            if (!r.IsActive) return null;   // 停用的不參與級距

            var tiers = others
                .Select(f => new ApprovalTierValidator.Tier(f.MinValue, f.MaxValue))
                .Append(new ApprovalTierValidator.Tier(r.MinValue, r.MaxValue))
                .ToList();

            var err = ApprovalTierValidator.Validate(tiers);
            return err is null ? null : BadRequest(new { message = err, code = "tier_invalid" });
        }

        private static string KindLabel(ApprovalConditionKind k) => k switch
        {
            ApprovalConditionKind.Days => "天數",
            ApprovalConditionKind.Hours => "時數",
            ApprovalConditionKind.Amount => "金額",
            _ => "無條件",
        };
    }
}
