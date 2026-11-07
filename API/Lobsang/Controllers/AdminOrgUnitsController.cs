using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using Lobsang.Models;
using Lobsang.Services;
using System.Security.Claims;

namespace Lobsang.Controllers
{
    public record SaveOrgUnitRequest(string Code, string Name, int? ParentId, string Kind, int? ManagerUserId,
        int SortOrder, bool IsActive);

    public record OrgUnitNode(int Id, string Code, string Name, string Kind, int? ManagerUserId, string? ManagerName,
        int SortOrder, bool IsActive, int UserCount, List<OrgUnitNode> Children);

    /// <summary>組織編制維護：簽核鏈與資料可視範圍的依據</summary>
    [ApiController]
    [Route("api/admin/org-units")]
    [RequirePermission("org.manage")]
    public class AdminOrgUnitsController(AppDbContext db, AuditLogger audit) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);

        [HttpGet]
        public async Task<IActionResult> Tree()
        {
            var units = await db.OrgUnits.Where(o => o.InstitutionId == InstitutionId)
                .OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
                .ToListAsync();

            var managerNames = await db.Users
                .Where(u => u.InstitutionId == InstitutionId)
                .ToDictionaryAsync(u => u.Id, u => u.Name);

            var userCounts = await db.Users
                .Where(u => u.InstitutionId == InstitutionId && u.OrgUnitId != null)
                .GroupBy(u => u.OrgUnitId!.Value)
                .Select(g => new { OrgUnitId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.OrgUnitId, x => x.Count);

            return Ok(BuildTree(units, null, managerNames, userCounts));
        }

        /// <summary>指派單位主管用的人員下拉</summary>
        [HttpGet("user-options")]
        public async Task<IActionResult> UserOptions() =>
            Ok(await db.Users.Where(u => u.InstitutionId == InstitutionId && u.IsActive)
                .OrderBy(u => u.SortOrder).ThenBy(u => u.Id)
                .Select(u => new { id = u.Id, name = u.Name, account = u.Account, title = u.Title })
                .ToListAsync());

        [HttpPost]
        public async Task<IActionResult> Create(SaveOrgUnitRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "單位代碼與名稱為必填", code = "required" });
            if (await db.OrgUnits.AnyAsync(o => o.InstitutionId == InstitutionId && o.Code == request.Code))
                return BadRequest(new { message = $"單位代碼「{request.Code}」已存在", code = "duplicate_code" });

            var sortOrder = request.SortOrder > 0
                ? request.SortOrder
                : (await db.OrgUnits.Where(o => o.ParentId == request.ParentId).MaxAsync(o => (int?)o.SortOrder) ?? 0) + 1;

            var unit = new OrgUnit
            {
                InstitutionId = InstitutionId,
                ParentId = request.ParentId,
                Code = request.Code.Trim(),
                Name = request.Name.Trim(),
                Kind = string.IsNullOrWhiteSpace(request.Kind) ? "部門" : request.Kind,
                ManagerUserId = request.ManagerUserId,
                SortOrder = sortOrder,
                IsActive = request.IsActive,
            };
            db.OrgUnits.Add(unit);
            audit.Log("org_unit.create", "org_unit", null, null, new { request.Code, request.Name, request.Kind, request.ParentId });
            await db.SaveChangesAsync();
            return Ok(new { id = unit.Id });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, SaveOrgUnitRequest request)
        {
            var unit = await db.OrgUnits.FirstOrDefaultAsync(o => o.Id == id && o.InstitutionId == InstitutionId);
            if (unit is null) return NotFound();
            if (request.ParentId == id)
                return BadRequest(new { message = "上級單位不可為自己", code = "invalid_parent" });
            if (request.ParentId.HasValue && await IsDescendant(id, request.ParentId.Value))
                return BadRequest(new { message = "上級單位不可為自己的下層單位", code = "circular_parent" });
            if (await db.OrgUnits.AnyAsync(o => o.InstitutionId == InstitutionId && o.Code == request.Code && o.Id != id))
                return BadRequest(new { message = $"單位代碼「{request.Code}」已存在", code = "duplicate_code" });

            /* ⚠⚠ 停用前要先搬空：若停用只是一個開關 ——
             *   單位會從下拉選單消失，但簽核、可見範圍照樣在用它，而且沒有提示。 */
            if (unit.IsActive && !request.IsActive)
            {
                var blockers = await DeactivationBlockersAsync(id, request.ManagerUserId);
                if (blockers.Count > 0)
                    return BadRequest(new
                    {
                        message = $"停用前請先處理：{string.Join("；", blockers)}。",
                        code = "unit_not_empty",
                    });
            }

            var before = new { unit.Code, unit.Name, unit.Kind, unit.ParentId, unit.ManagerUserId,
                unit.SortOrder, unit.IsActive };

            unit.Code = request.Code.Trim();
            unit.Name = request.Name.Trim();
            unit.Kind = request.Kind;
            unit.ParentId = request.ParentId;
            unit.ManagerUserId = request.ManagerUserId;
            unit.SortOrder = request.SortOrder;
            unit.IsActive = request.IsActive;

            audit.Log("org_unit.update", "org_unit", id, before, request);
            await db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var unit = await db.OrgUnits.FirstOrDefaultAsync(o => o.Id == id && o.InstitutionId == InstitutionId);
            if (unit is null) return NotFound();

            var childCount = await db.OrgUnits.CountAsync(o => o.ParentId == id);
            if (childCount > 0)
                return BadRequest(new { message = $"此單位尚有 {childCount} 個下層單位，請先移除", code = "unit_has_children" });

            var userCount = await db.Users.CountAsync(u => u.OrgUnitId == id);
            if (userCount > 0)
                return BadRequest(new { message = $"仍有 {userCount} 位人員歸屬此單位，請先調整後再刪除", code = "unit_in_use" });

            /* ⚠⚠ 刪除只給「從沒用過」的單位（建錯的）。
             *   用過的單位一律改用「停用」，歷史照樣顯示原本的單位名稱。
             *   ⭐ 新專案的資料表若有 OrgUnitId，要加進 HistoryUsageAsync，否則刪單位會讓那些紀錄失去單位。 */
            var used = await HistoryUsageAsync(id);
            if (used.Count > 0)
                return BadRequest(new
                {
                    message = $"這個單位已經有資料在用（{string.Join("、", used)}），刪除會讓這些紀錄失去單位。請改用「停用」。",
                    code = "unit_has_history",
                });

            audit.Log("org_unit.delete", "org_unit", id, new { unit.Code, unit.Name }, null);
            db.OrgUnits.Remove(unit);
            await db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// 這個單位被哪些「歷史資料」用過（刪除前檢查）。⚠ 含已刪除（軟刪）的資料 —— 刪單位會讓它們的單位變空白。
        /// </summary>
        private async Task<List<string>> HistoryUsageAsync(int id)
        {
            var list = new List<string>();
            async Task Add(string label, Task<int> count) { var n = await count; if (n > 0) list.Add($"{label} {n} 筆"); }
            await Add("單位主管設定", db.OrgUnitManagers.CountAsync(m => m.OrgUnitId == id));
            return list;
        }

        /// <summary>
        /// 停用前必須搬空的東西。
        /// ⚠ 不採「停用了就讓簽核默默往上一層找主管」——那會悄悄換掉主管路線。
        ///   所以停用前要求先搬空，停用後就沒有人會從它出發。
        /// </summary>
        /// <param name="managerAfter">這次存檔後的單位主管（同一次可以先清掉主管再停用）</param>
        private async Task<List<string>> DeactivationBlockersAsync(int id, int? managerAfter)
        {
            var list = new List<string>();
            var users = await db.Users.Where(u => u.OrgUnitId == id && u.IsActive)
                .Select(u => u.Name).ToListAsync();
            if (users.Count > 0)
                list.Add($"在職人員 {users.Count} 位要先調到別的單位（{string.Join("、", users.Take(5))}{(users.Count > 5 ? "…" : "")}）");

            var children = await db.OrgUnits.Where(o => o.ParentId == id && o.IsActive)
                .Select(o => o.Name).ToListAsync();
            if (children.Count > 0)
                list.Add($"下層單位要先改掛或停用（{string.Join("、", children)}）");

            var managers = await db.OrgUnitManagers.CountAsync(m => m.OrgUnitId == id);
            if (managerAfter is not null || managers > 0)
                list.Add("單位主管設定要先清掉（簽核鏈仍會用到）");

            return list;
        }

        /// <summary>candidate 是否為 unitId 的下層（防止把父層設成自己的子孫造成循環）</summary>
        private async Task<bool> IsDescendant(int unitId, int candidateId)
        {
            var units = await db.OrgUnits.Where(o => o.InstitutionId == InstitutionId)
                .Select(o => new { o.Id, o.ParentId }).ToListAsync();
            var parentOf = units.ToDictionary(o => o.Id, o => o.ParentId);

            var cursor = parentOf.GetValueOrDefault(candidateId);
            var guard = 0;
            while (cursor.HasValue && guard++ < 100)
            {
                if (cursor.Value == unitId) return true;
                cursor = parentOf.GetValueOrDefault(cursor.Value);
            }
            return false;
        }

        private static List<OrgUnitNode> BuildTree(List<OrgUnit> units, int? parentId,
            Dictionary<int, string> managerNames, Dictionary<int, int> userCounts) =>
            units.Where(o => o.ParentId == parentId)
                .Select(o => new OrgUnitNode(o.Id, o.Code, o.Name, o.Kind, o.ManagerUserId,
                    o.ManagerUserId.HasValue ? managerNames.GetValueOrDefault(o.ManagerUserId.Value) : null,
                    o.SortOrder, o.IsActive, userCounts.GetValueOrDefault(o.Id),
                    BuildTree(units, o.Id, managerNames, userCounts)))
                .ToList();
    }
}
