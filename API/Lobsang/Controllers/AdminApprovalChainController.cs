using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using Lobsang.Models;
using Lobsang.Services;

namespace Lobsang.Controllers
{
    public record SaveUnitManagerRequest(
        int OrgUnitId, int UserId, OrgUnitManagerKind Kind, int SortOrder, string? TitleNote);

    /// <summary>
    /// 送簽時指定各關的簽核人。
    ///
    /// ⚠ 用陣列而不是字典：JSON 的物件鍵一定是字串，
    /// <c>{"1": 5}</c> 在 .NET 反序列化成 <c>Dictionary&lt;int,int&gt;</c> 會依環境而異，
    /// 用明確的陣列不必賭。
    /// ⚠ 指定的人**由後端驗證是否在候選名單內**（見 <c>ApprovalResolver.ResolveAssignmentsAsync</c>），
    /// 不驗就等於申請人可以指定任何人來簽自己的單。
    /// </summary>
    public record SubmitWithAssigneesRequest(List<StepAssigneePick>? Assignees)
    {
        public IReadOnlyDictionary<int, int>? AssigneeMap =>
            Assignees is null || Assignees.Count == 0
                ? null
                : Assignees
                    .GroupBy(a => a.StepOrder)
                    .ToDictionary(g => g.Key, g => g.Last().UserId);
    }

    public record StepAssigneePick(int StepOrder, int UserId);

    /// <summary>
    /// 簽核鏈預覽與單位主管／督導維護。
    ///
    /// ⚠ 這一支存在的理由是民生醫院的組織形狀：
    /// <list type="bullet">
    /// <item>91 病房的簽核主管是「副護理長**或**護理長」，而副護理長可能多位 ——
    ///   他們是**同一關的候選人**，申請時由申請人挑一位；</item>
    /// <item>督導長橫跨數個平行病房、**不簽核但要看得到** ——
    ///   組織樹一個單位只有一個上層，表達不了這種關係。</item>
    /// </list>
    /// 兩者都靠 <see cref="OrgUnitManager"/>，差別只在 <see cref="OrgUnitManagerKind"/>。
    /// </summary>
    [ApiController]
    [Route("api/admin/approval-chain")]
    public class AdminApprovalChainController(
        AppDbContext db, ApprovalResolver approvals, AuditLogger audit) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);
        private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private bool IsAdmin => User.HasPermission("system.admin");

        /// <summary>
        /// 送簽前預覽：這張單會經過哪幾關、每一關有誰可簽、哪幾關可以自己挑。
        /// </summary>
        /// <param name="formCode">表單代碼（<c>leave-request</c>／<c>overtime</c>／<c>attendance-correction</c>）</param>
        /// <param name="userId">申請人；留空＝自己。代他人送簽時（人事）要傳</param>
        [HttpGet("preview")]
        public async Task<IActionResult> Preview(
            [FromQuery] string formCode, [FromQuery] int? userId = null, CancellationToken ct = default)
        {
            var target = userId ?? UserId;
            // 代他人預覽等於看得到別人的主管鏈；限本人、管理員或能管理帳號者（⭐ 新專案可加自己的代理送簽權限）
            if (target != UserId && !IsAdmin && !User.HasPermission("user.manage"))
                return StatusCode(403, new { message = "無權預覽他人的簽核鏈", code = "forbidden" });

            var formId = await db.FormDefinitions
                .Where(f => f.InstitutionId == InstitutionId && f.Code == formCode)
                .Select(f => (int?)f.Id).FirstOrDefaultAsync(ct);
            if (formId is null)
                return BadRequest(new { message = $"找不到表單設定「{formCode}」", code = "no_form" });

            var steps = await approvals.PreviewAsync(formId.Value, target);
            return Ok(new
            {
                formCode,
                userId = target,
                // 沒有任何關卡時要講出來 —— 送簽會失敗，但使用者在按下去之前就該知道
                usable = steps.Count > 0,
                steps = steps.Select(s => new
                {
                    s.StepOrder, s.StepName, s.Pickable, s.DefaultUserId,
                    candidates = s.Candidates.Select(c => new
                    {
                        c.UserId, c.Name, c.Title, c.UnitName,
                    }),
                }),
            });
        }

        // ===== 單位主管／督導維護 =====

        [HttpGet("unit-managers")]
        [RequirePermission("user.manage")]
        public async Task<IActionResult> List([FromQuery] int? orgUnitId = null, CancellationToken ct = default)
        {
            var q = db.OrgUnitManagers
                .Include(m => m.User)
                .Include(m => m.OrgUnit)
                .Where(m => m.InstitutionId == InstitutionId);
            if (orgUnitId is not null) q = q.Where(m => m.OrgUnitId == orgUnitId);

            var rows = await q.OrderBy(m => m.OrgUnitId).ThenBy(m => m.SortOrder).ThenBy(m => m.Id)
                .ToListAsync(ct);
            return Ok(rows.Select(m => new
            {
                m.Id, m.OrgUnitId, orgUnitName = m.OrgUnit?.Name,
                m.UserId, userName = m.User?.Name,
                kind = m.Kind.ToString(), m.SortOrder, m.TitleNote,
            }));
        }

        [HttpPost("unit-managers")]
        [RequirePermission("user.manage")]
        public async Task<IActionResult> Create(SaveUnitManagerRequest r, CancellationToken ct = default)
        {
            var unitOk = await db.OrgUnits.AnyAsync(
                o => o.Id == r.OrgUnitId && o.InstitutionId == InstitutionId, ct);
            if (!unitOk) return BadRequest(new { message = "單位不存在", code = "invalid_unit" });

            var userOk = await db.Users.AnyAsync(
                u => u.Id == r.UserId && u.InstitutionId == InstitutionId, ct);
            if (!userOk) return BadRequest(new { message = "人員不存在", code = "invalid_user" });

            // 同一人對同一單位只能有一種身分；改身分是更新不是再加一筆
            var exists = await db.OrgUnitManagers.FirstOrDefaultAsync(
                m => m.OrgUnitId == r.OrgUnitId && m.UserId == r.UserId, ct);
            if (exists is not null)
            {
                exists.Kind = r.Kind;
                exists.SortOrder = r.SortOrder;
                exists.TitleNote = r.TitleNote?.Trim();
                await db.SaveChangesAsync(ct);
                return Ok(new { id = exists.Id, updated = true });
            }

            var m = new OrgUnitManager
            {
                InstitutionId = InstitutionId,
                OrgUnitId = r.OrgUnitId,
                UserId = r.UserId,
                Kind = r.Kind,
                SortOrder = r.SortOrder,
                TitleNote = r.TitleNote?.Trim(),
            };
            db.OrgUnitManagers.Add(m);
            audit.Log("org_unit_manager.create", "org_unit_manager", null, null, new
            {
                r.OrgUnitId, r.UserId, kind = r.Kind.ToString(), r.TitleNote,
            });
            await db.SaveChangesAsync(ct);
            return Ok(new { id = m.Id });
        }

        [HttpDelete("unit-managers/{id:int}")]
        [RequirePermission("user.manage")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
        {
            var m = await db.OrgUnitManagers.FirstOrDefaultAsync(
                x => x.Id == id && x.InstitutionId == InstitutionId, ct);
            if (m is null) return NotFound();

            audit.Log("org_unit_manager.delete", "org_unit_manager", id,
                new { m.OrgUnitId, m.UserId, kind = m.Kind.ToString() }, null);
            db.OrgUnitManagers.Remove(m);
            await db.SaveChangesAsync(ct);
            return NoContent();
        }
    }
}
