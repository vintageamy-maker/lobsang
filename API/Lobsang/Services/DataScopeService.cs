using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;

namespace Lobsang.Services
{
    /// <summary>資料可見範圍的三個層級</summary>
    public enum DataScope
    {
        /// <summary>只看得到自己</summary>
        Self,
        /// <summary>看得到所屬單位**及其所有子單位**的人</summary>
        Unit,
        /// <summary>全院</summary>
        All,
    }

    /// <summary>
    /// 「這個人看得到誰」的**單一判定點**。
    ///
    /// 改版前這件事散落在四個地方、四種寫法：有的把 <c>leave.manage</c> 當旗標、
    /// 有的把 <c>schedule.write</c> 當旗標、有的直接比 <c>OrgUnitId ==</c>。
    /// 平面比對是「保育組長看不到掛在小家底下的組員」的根因 —— 組長的單位是「保育組」，
    /// 組員的單位是「如意家」，兩者不相等，但如意家是保育組的子單位。
    ///
    /// <see cref="DescendantUnitIdsAsync"/> 是 <see cref="ApprovalResolver.BuildManagerChainAsync"/>
    /// 的對偶：後者沿 ParentId 往上找主管，前者沿 ParentId 往下展開子樹。
    ///
    /// 註：JWT 目前沒有 orgUnitId claim，判定必須查 DB。本服務註冊為 Scoped 並在
    /// request 生命週期內快取，同一次請求問幾次都只查一次。
    /// </summary>
    public class DataScopeService(AppDbContext db)
    {
        private readonly Dictionary<int, HashSet<int>> _descendantCache = [];
        private List<UnitRow>? _units;
        private List<ManagerRow>? _managers;
        private int _unitsInstitutionId = -1;

        private record UnitRow(int Id, int? ParentId, int? ManagerUserId);
        private record ManagerRow(int OrgUnitId, int UserId);

        private async Task<List<UnitRow>> UnitsAsync(int institutionId)
        {
            if (_units is not null && _unitsInstitutionId == institutionId) return _units;
            _units = await db.OrgUnits
                .Where(o => o.InstitutionId == institutionId)
                .Select(o => new UnitRow(o.Id, o.ParentId, o.ManagerUserId))
                .ToListAsync();
            _unitsInstitutionId = institutionId;
            _managers = null;
            return _units;
        }

        /// <summary>
        /// 多對多的單位管理關係。
        /// ⚠ <b>Approver 與 Viewer 都算</b> —— 可見範圍看的是「有沒有管理關係」，
        /// 不是「要不要簽核」。民生醫院的督導長橫跨數個平行病房、不簽核，
        /// 但必須看得到所轄病房的班表與請假；組織樹一個單位只有一個上層，
        /// 表達不了「A、B、C 三個病房歸同一位督導長」，只能靠這張表。
        /// </summary>
        private async Task<List<ManagerRow>> ManagersAsync(int institutionId)
        {
            await UnitsAsync(institutionId);
            _managers ??= await db.OrgUnitManagers
                .Where(m => m.InstitutionId == institutionId)
                .Select(m => new ManagerRow(m.OrgUnitId, m.UserId))
                .ToListAsync();
            return _managers;
        }

        private static int CurrentUserId(ClaimsPrincipal user) =>
            int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

        /// <summary>
        /// 判定層級。<paramref name="manageCode"/> 是該模組的「全院權限」代碼
        /// （請假＝leave.manage、排班＝schedule.write）。
        /// </summary>
        public async Task<DataScope> ScopeOfAsync(
            ClaimsPrincipal user, int institutionId, string manageCode)
        {
            if (user.HasPermission(manageCode)) return DataScope.All;

            var me = CurrentUserId(user);
            if (me == 0) return DataScope.Self;

            var units = await UnitsAsync(institutionId);
            if (units.Any(u => u.ManagerUserId == me)) return DataScope.Unit;

            var managers = await ManagersAsync(institutionId);
            return managers.Any(m => m.UserId == me) ? DataScope.Unit : DataScope.Self;
        }

        /// <summary>
        /// 自 <paramref name="rootUnitId"/> 往下展開整棵子樹（含自己）。
        /// 迴圈防呆：組織樹被誤設成環時仍會終止。
        /// </summary>
        public async Task<HashSet<int>> DescendantUnitIdsAsync(int institutionId, int rootUnitId)
        {
            if (_descendantCache.TryGetValue(rootUnitId, out var cached)) return cached;

            var units = await UnitsAsync(institutionId);
            var childrenOf = units
                .Where(u => u.ParentId.HasValue)
                .GroupBy(u => u.ParentId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(u => u.Id).ToList());

            var result = new HashSet<int> { rootUnitId };
            var queue = new Queue<int>();
            queue.Enqueue(rootUnitId);
            var guard = 0;
            while (queue.Count > 0 && guard++ < 1000)
            {
                var cur = queue.Dequeue();
                if (!childrenOf.TryGetValue(cur, out var kids)) continue;
                foreach (var k in kids)
                    if (result.Add(k)) queue.Enqueue(k);
            }

            _descendantCache[rootUnitId] = result;
            return result;
        }

        /// <summary>
        /// 此人身為主管或督導所轄的所有單位（含各自的子樹）。
        /// 兩個來源聯集：<see cref="OrgUnit.ManagerUserId"/>（單一主管，育幼院沿用）
        /// 與 <c>org_unit_managers</c>（多對多，含督導長）。
        /// </summary>
        public async Task<HashSet<int>> ManagedUnitIdsAsync(ClaimsPrincipal user, int institutionId)
        {
            var me = CurrentUserId(user);
            var units = await UnitsAsync(institutionId);
            var managers = await ManagersAsync(institutionId);

            var roots = units.Where(u => u.ManagerUserId == me).Select(u => u.Id)
                .Concat(managers.Where(m => m.UserId == me).Select(m => m.OrgUnitId))
                .Distinct();

            var result = new HashSet<int>();
            foreach (var id in roots)
                result.UnionWith(await DescendantUnitIdsAsync(institutionId, id));
            return result;
        }

        /// <summary>
        /// 可見的人員 id 集合。
        /// <c>null</c> 代表**全院、不需過濾** —— 呼叫端據此決定要不要加 Where，
        /// 避免全院查詢時硬塞一個上千筆的 IN 清單。
        /// </summary>
        public async Task<HashSet<int>?> VisibleUserIdsAsync(
            ClaimsPrincipal user, int institutionId, string manageCode)
        {
            var scope = await ScopeOfAsync(user, institutionId, manageCode);
            if (scope == DataScope.All) return null;

            var me = CurrentUserId(user);
            var ids = new HashSet<int> { me };   // ⚠ 主管一定看得到自己（自己未必掛在所轄單位下）
            if (scope == DataScope.Self) return ids;

            var unitIds = await ManagedUnitIdsAsync(user, institutionId);
            if (unitIds.Count == 0) return ids;

            var members = await db.Users
                .Where(u => u.InstitutionId == institutionId
                            && u.OrgUnitId != null && unitIds.Contains(u.OrgUnitId.Value))
                .Select(u => u.Id)
                .ToListAsync();
            ids.UnionWith(members);
            return ids;
        }

        /// <summary>能否查看某一個人的資料</summary>
        public async Task<bool> CanSeeUserAsync(
            ClaimsPrincipal user, int institutionId, string manageCode, int targetUserId)
        {
            if (targetUserId == CurrentUserId(user)) return true;
            var visible = await VisibleUserIdsAsync(user, institutionId, manageCode);
            return visible is null || visible.Contains(targetUserId);
        }

        /// <summary>
        /// 能否以某個單位作為查詢條件。
        /// ⚠ 這一支是修既有越權漏洞用的：排班端點原本直接吃前端傳來的 orgUnitId 而不校驗，
        /// 任何登入者改一下網址就撈得到別單位的班表。
        /// </summary>
        public async Task<bool> CanQueryUnitAsync(
            ClaimsPrincipal user, int institutionId, string manageCode, int orgUnitId)
        {
            var scope = await ScopeOfAsync(user, institutionId, manageCode);
            if (scope == DataScope.All) return true;
            if (scope == DataScope.Self) return false;
            return (await ManagedUnitIdsAsync(user, institutionId)).Contains(orgUnitId);
        }

        /// <summary>是否為全院範圍（沒有特定對象可比對時用，例如「未對應人員的打卡」）</summary>
        public async Task<bool> CanQueryAllAsync(
            ClaimsPrincipal user, int institutionId, string manageCode) =>
            await ScopeOfAsync(user, institutionId, manageCode) == DataScope.All;

        /// <summary>供前端顯示「你目前看得到的範圍」用的說明文字</summary>
        public static string Describe(DataScope scope) => scope switch
        {
            DataScope.All => "全院",
            DataScope.Unit => "所轄單位",
            _ => "個人",
        };
    }
}
