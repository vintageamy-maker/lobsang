using Microsoft.EntityFrameworkCore;
using Lobsang.Data;
using Lobsang.Models;

namespace Lobsang.Services
{
    /// <summary>
    /// 單據種類代碼。⚠ 這些字串會出現在前端與稽核紀錄裡，不可隨意改字。
    /// ⭐ 新專案加了自己的單據（有自己的簽核快照表，例如請假單），要在三個地方一起補：
    ///   這裡加代碼、<see cref="UserDeactivationService.InspectAsync"/> 加一段查詢、
    ///   <see cref="UserDeactivationService.ReassignAsync"/> 的 switch 加一個 case。
    ///   漏了任何一處，停用帳號時那種單就會靜靜卡在離職的人身上。
    /// </summary>
    public static class ApprovalDocType
    {
        public const string Record = "record";

        public static string Label(string code) => code switch
        {
            Record => "表單",
            _ => code,
        };
    }

    /// <summary>還等著這個人簽的一關</summary>
    public record PendingStepRef(
        string DocType, int DocId, string DocLabel, int StepId,
        int Round, int StepOrder, string StepName,
        string ApplicantName, DateTime? SubmittedAt);

    /// <summary>這個人是哪些單位的簽核主管</summary>
    public record UnitManagerRef(int OrgUnitId, string UnitName, string Source, int? ManagerRowId);

    /// <summary>牽涉到這個人的代理設定</summary>
    public record DelegationRef(
        int Id, string Side, string CounterpartName, DateOnly StartDate, DateOnly EndDate);

    /// <summary>流程設定裡把他寫死成簽核人的關卡</summary>
    public record SpecificStepRef(int StepId, string FormName, int StepOrder);

    /// <summary>
    /// 停用一個帳號會造成什麼後果。
    ///
    /// ⚠⚠ **四類要一起看，不能只看第一類。**
    ///   只把「在途的單」改派掉，下一張新單子還是會送到同一個人手上 ——
    ///   因為他在組織編制上仍然是某個單位的主管（第二類）。
    ///   ⭐ 這是設計這支服務時最容易漏掉的一塊：
    ///     第一類是「現在卡住的」，第二、四類是「以後還會再卡」。
    /// </summary>
    public record DeactivationImpact(
        int UserId,
        string Name,
        List<PendingStepRef> PendingSteps,
        List<UnitManagerRef> UnitManagerOf,
        List<DelegationRef> Delegations,
        List<SpecificStepRef> SpecificSteps)
    {
        /// <summary>
        /// ⚠ 「加簽檢視者」**不列入**擋人的條件。
        ///
        /// 檢視者（督導長那種）只是看得到，**不進簽核鏈** ——
        /// 他停用不會讓任何一張單卡住。硬要求先清掉只是無謂的阻擋。
        /// ⭐ 但仍然列出來給人看，因為那是該順手清掉的殘留設定。
        /// </summary>
        public const string ViewerSource = "加簽檢視者";

        public int Total =>
            PendingSteps.Count + UnitManagerOf.Count + Delegations.Count + SpecificSteps.Count;

        /// <summary>真正會造成卡單的項數（檢視者不算）</summary>
        public int BlockingCount =>
            PendingSteps.Count
            + UnitManagerOf.Count(m => m.Source != ViewerSource)
            + Delegations.Count
            + SpecificSteps.Count;

        public bool Blocked => BlockingCount > 0;
    }

    /// <summary>改派一關。DocType 見 <see cref="ApprovalDocType"/></summary>
    public record ReassignItem(string DocType, int StepId, int ToUserId);

    /// <summary>
    /// 停用帳號前的簽核殘留檢查與改派。
    ///
    /// ── 為什麼需要這支 ─────────────────────────────────────────────
    /// 送簽當下就把簽核人**解析成具體的人**寫死在單上（快照），
    /// 這是刻意的 —— 組織異動不該改變在途單據的簽核人，否則稽核解釋不了。
    /// 代價就是：**那個人一旦停用，單子沒有任何機制會自己脫困**，
    /// 而且畫面上完全看不出異常，只是靜靜躺在那裡沒人簽。
    ///
    /// ⚠ 現況下唯一的補救是「管理員可以簽掉任何一關」（ApprovalResolver.CanAct），
    ///   但那要**有人先發現**。這支服務就是讓系統在源頭先發現。
    /// </summary>
    public class UserDeactivationService(AppDbContext db)
    {
        /// <summary>
        /// 這個人身上還掛著什麼。
        /// ⚠ 只看**還在簽核中**的單：已完成／已撤銷／退回草稿的單即使留著 Pending 關卡，
        ///   也不會再送到任何人手上，列出來只是雜訊。
        /// </summary>
        public async Task<DeactivationImpact> InspectAsync(int institutionId, int userId)
        {
            var user = await db.Users
                .Where(u => u.Id == userId && u.InstitutionId == institutionId)
                .Select(u => new { u.Id, u.Name })
                .FirstOrDefaultAsync();

            if (user is null)
                return new DeactivationImpact(userId, $"#{userId}", [], [], [], []);

            var pending = new List<PendingStepRef>();
            pending.AddRange(await RecordStepsAsync(institutionId, userId));

            return new DeactivationImpact(
                user.Id, user.Name,
                [.. pending.OrderBy(p => p.DocType).ThenBy(p => p.DocId)],
                await UnitManagerAsync(institutionId, userId),
                await DelegationsAsync(institutionId, userId),
                await SpecificStepsAsync(institutionId, userId));
        }

        // ── 第一類：在途的單 ──────────────────────────────────────────
        // ⚠ 各單據的快照表結構相同但型別不同（EF 沒有共同基底可查），只能各查一次。
        //    ⭐ 判準與 ApprovalResolver.PendingStep 一致：**最新一輪** ＋ Pending。
        //    舊輪次的 Pending 是退件時留下的殘影，不會再送給任何人。

        private async Task<List<PendingStepRef>> RecordStepsAsync(int inst, int uid) =>
            [.. (await (
                from s in db.WorkRecordApprovalSteps
                join w in db.WorkRecords on s.WorkRecordId equals w.Id
                join f in db.FormDefinitions on w.FormDefinitionId equals f.Id
                where w.InstitutionId == inst
                      && w.Status == RecordStatus.InReview
                      && w.DeletedAt == null
                      && s.AssignedUserId == uid
                      && s.Status == ApprovalStepStatus.Pending
                      && s.Round == db.WorkRecordApprovalSteps
                            .Where(x => x.WorkRecordId == w.Id).Max(x => x.Round)
                    // ⚠ 工作記錄沒有 UserId，填寫者記在 CreatedBy。
                    //    用 left join 一次撈完，不要在迴圈裡逐筆查（N+1）。
                from au in db.Users.Where(u => u.Id == w.CreatedBy).DefaultIfEmpty()
                select new
                {
                    w.Id, s.Round, s.StepOrder, StepName = s.AssignedUserName ?? s.RoleName,
                    Applicant = au != null ? au.Name : null,
                    w.SubmittedAt, Form = f.Name, w.RecordDate,
                    StepId = s.Id,
                }).ToListAsync())
                .Select(x => new PendingStepRef(
                    ApprovalDocType.Record, x.Id, $"{x.Form} {x.RecordDate:MM/dd}",
                    x.StepId, x.Round, x.StepOrder, x.StepName ?? "未指派",
                    x.Applicant ?? "—", x.SubmittedAt))];

        private async Task<List<UnitManagerRef>> UnitManagerAsync(int inst, int uid)
        {
            var byUnit = await db.OrgUnits
                .Where(o => o.InstitutionId == inst && o.IsActive && o.ManagerUserId == uid)
                .Select(o => new UnitManagerRef(o.Id, o.Name, "編制主管", null))
                .ToListAsync();

            var extra = await db.OrgUnitManagers
                .Where(m => m.InstitutionId == inst && m.UserId == uid)
                .Select(m => new UnitManagerRef(
                    m.OrgUnitId, m.OrgUnit!.Name,
                    m.Kind == OrgUnitManagerKind.Approver ? "加簽簽核人" : DeactivationImpact.ViewerSource,
                    m.Id))
                .ToListAsync();

            return [.. byUnit, .. extra];
        }

        // ── 第三類：代理設定（他代別人，或別人代他）──
        private async Task<List<DelegationRef>> DelegationsAsync(int inst, int uid) =>
            await db.UserDelegations
                .Where(d => d.InstitutionId == inst && d.IsActive
                            && (d.PrincipalUserId == uid || d.DelegateUserId == uid))
                .Select(d => new DelegationRef(
                    d.Id,
                    d.PrincipalUserId == uid ? "他被代理" : "他代理別人",
                    d.PrincipalUserId == uid ? d.Delegate!.Name : d.Principal!.Name,
                    d.StartDate, d.EndDate))
                .ToListAsync();

        // ── 第四類：流程設定把他寫死成簽核人 ──
        private async Task<List<SpecificStepRef>> SpecificStepsAsync(int inst, int uid) =>
            await db.FormApprovalSteps
                .Where(s => s.SpecificUserId == uid
                            && s.ApprovalFlow!.FormDefinition!.InstitutionId == inst)
                .Select(s => new SpecificStepRef(
                    s.Id, s.ApprovalFlow!.FormDefinition!.Name, s.StepOrder))
                .ToListAsync();

        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 把在途單據上指名給 <paramref name="fromUserId"/> 的待簽關卡改派給別人。
        ///
        /// ⚠ 只動 <b>Pending</b> 的關卡 —— 已經簽過的是既成事實，不可回頭改。
        /// ⚠ 不動角色關卡（AssignedUserId 為 null）：那種關卡本來就不綁特定人，
        ///   停用一個人不會讓它卡住。
        /// ⚠ 呼叫端負責 SaveChanges 與寫稽核（這裡只改實體，回傳改了哪些）。
        /// </summary>
        /// <param name="items">
        /// 指定要改哪幾關。傳 null ＝ 全部改給 <paramref name="fallbackToUserId"/>。
        /// </param>
        public async Task<List<(string DocType, int StepId, string From, string To)>> ReassignAsync(
            int institutionId, int fromUserId, int? fallbackToUserId, List<ReassignItem>? items)
        {
            var impact = await InspectAsync(institutionId, fromUserId);
            var targets = items is { Count: > 0 }
                ? items
                : fallbackToUserId is int all
                    ? [.. impact.PendingSteps.Select(p => new ReassignItem(p.DocType, p.StepId, all))]
                    : [];

            if (targets.Count == 0) return [];

            var toIds = targets.Select(t => t.ToUserId).Distinct().ToList();
            var people = await db.Users
                .Where(u => toIds.Contains(u.Id) && u.InstitutionId == institutionId)
                .ToDictionaryAsync(u => u.Id, u => u);

            // ⚠⚠ 改派給一個「也已經停用」的人，等於把問題原地搬家 —— 明確擋下。
            foreach (var t in targets)
            {
                if (!people.TryGetValue(t.ToUserId, out var p))
                    throw new InvalidOperationException($"找不到要改派的對象（#{t.ToUserId}）。");
                if (!p.IsActive)
                    throw new InvalidOperationException($"「{p.Name}」已經停用，不能當簽核人。");
                if (t.ToUserId == fromUserId)
                    throw new InvalidOperationException("改派的對象不能是原本那個人。");
            }

            var fromName = await db.Users.Where(u => u.Id == fromUserId)
                .Select(u => u.Name).FirstOrDefaultAsync() ?? $"#{fromUserId}";

            var done = new List<(string, int, string, string)>();

            foreach (var g in targets.GroupBy(t => t.DocType))
            {
                var ids = g.Select(x => x.StepId).ToList();
                var map = g.ToDictionary(x => x.StepId, x => x.ToUserId);

                switch (g.Key)
                {
                    case ApprovalDocType.Record:
                        Apply(await db.WorkRecordApprovalSteps
                            .Where(s => ids.Contains(s.Id)).ToListAsync());
                        break;
                    default:
                        throw new InvalidOperationException($"不認得的單據種類：{g.Key}");
                }

                void Apply<T>(List<T> steps) where T : class, IApprovalStep
                {
                    foreach (var s in steps)
                    {
                        // ⚠ 防呆：只改「還沒簽」且「原本就是他」的關卡。
                        //    前端送上來的 stepId 不可信，這裡要再驗一次。
                        if (s.Status != ApprovalStepStatus.Pending) continue;
                        if (s.AssignedUserId != fromUserId) continue;

                        var to = people[map[s.Id]];
                        s.AssignedUserId = to.Id;
                        s.AssignedUserName = to.Name;
                        done.Add((g.Key, s.Id, fromName, to.Name));
                    }
                }
            }

            return [.. done];
        }
    }
}
