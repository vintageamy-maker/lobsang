using Microsoft.EntityFrameworkCore;
using Lobsang.Data;
using Lobsang.Models;

namespace Lobsang.Services
{
    /// <summary>
    /// 使用者在簽核情境下的有效身分：自己的角色 ＋ 代理來的角色/身分。
    /// </summary>
    public class ApprovalContext
    {
        public int UserId { get; init; }
        public bool IsAdmin { get; init; }

        /// <summary>自己擁有的角色</summary>
        public List<int> OwnRoleIds { get; init; } = [];

        /// <summary>目前有效代理的對象（被代理人 id）</summary>
        public List<int> PrincipalIds { get; init; } = [];

        /// <summary>代理來的角色 → 被代理人 id（用於判定「代誰簽」）</summary>
        public Dictionary<int, int> RoleViaPrincipal { get; init; } = [];

        /// <summary>自己＋代理來的角色聯集（查待簽清單用）</summary>
        public List<int> EffectiveRoleIds =>
            OwnRoleIds.Concat(RoleViaPrincipal.Keys).Distinct().ToList();
    }

    /// <summary>
    /// 簽核關卡的解析與授權判斷。
    /// 關卡設定（FormApprovalStep）在送簽時解析成快照（WorkRecordApprovalStep），
    /// 之後的授權一律以快照為準 — 組織異動不影響已送簽單據。
    /// </summary>
    public class ApprovalResolver(AppDbContext db)
    {
        /// <summary>
        /// 本輪待簽的關卡。工作記錄、請假、加班、補卡四種單據原本各自有一份
        /// 幾乎一模一樣的實作 —— 四份會走鐘的判準，最後會變成
        /// 「首頁說有 3 件、點進去只有 1 件」。收成一份。
        ///
        /// <paramref name="currentStep"/> 有值時精確定位到該關卡（工作記錄與請假單的原有語意）；
        /// 不給則取本輪第一個待簽關卡（加班與補卡的原有語意）。
        /// ⚠ 兩者在資料正常時等價，**但刻意保留差異** —— 統一語意等於改變既有端點的行為，
        /// 那是另一件事，不該混在這次的收斂裡。
        /// </summary>
        public static TStep? PendingStep<TStep>(IEnumerable<TStep> steps, int? currentStep = null)
            where TStep : class, IApprovalStep
        {
            var list = steps as IList<TStep> ?? [.. steps];
            if (list.Count == 0) return null;
            var round = list.Max(s => s.Round);
            return list
                .Where(s => s.Round == round && s.Status == ApprovalStepStatus.Pending
                            && (currentStep is null || s.StepOrder == currentStep))
                .OrderBy(s => s.StepOrder)
                .FirstOrDefault();
        }

        /// <summary>組出使用者的簽核身分（含有效代理）</summary>
        public async Task<ApprovalContext> BuildContextAsync(int userId, bool isAdmin)
        {
            var ownRoleIds = await db.UserRoles.Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId).ToListAsync();

            var principalIds = await ActiveDelegationPrincipalsAsync(userId);

            var roleViaPrincipal = new Dictionary<int, int>();
            if (principalIds.Count > 0)
            {
                var principalRoles = await db.UserRoles
                    .Where(ur => principalIds.Contains(ur.UserId))
                    .Select(ur => new { ur.RoleId, ur.UserId })
                    .ToListAsync();
                foreach (var pr in principalRoles)
                    roleViaPrincipal.TryAdd(pr.RoleId, pr.UserId);
            }

            return new ApprovalContext
            {
                UserId = userId,
                IsAdmin = isAdmin,
                OwnRoleIds = ownRoleIds,
                PrincipalIds = principalIds,
                RoleViaPrincipal = roleViaPrincipal,
            };
        }

        /// <summary>目前有效（日期區間內且啟用）的代理關係中，此人代理了誰</summary>
        public async Task<List<int>> ActiveDelegationPrincipalsAsync(int userId)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return await db.UserDelegations
                .Where(d => d.DelegateUserId == userId && d.IsActive
                            && d.StartDate <= today && d.EndDate >= today)
                .Select(d => d.PrincipalUserId)
                .Distinct()
                .ToListAsync();
        }

        /// <summary>
        /// 代理人可繼承的權限碼（排除 IsDelegable=false 的敏感權限，如薪資）。
        /// 於登入簽發 JWT 時併入 perm claims。
        /// </summary>
        public async Task<List<string>> DelegatedPermissionCodesAsync(int userId)
        {
            var principalIds = await ActiveDelegationPrincipalsAsync(userId);
            if (principalIds.Count == 0) return [];

            return await db.UserRoles
                .Where(ur => principalIds.Contains(ur.UserId))
                .SelectMany(ur => ur.Role!.RolePermissions)
                .Where(rp => rp.Permission!.IsDelegable)
                .Select(rp => rp.Permission!.Code)
                .Distinct()
                .ToListAsync();
        }

        /// <summary>
        /// 這張單該走哪一條流程。
        ///
        /// ⭐ 級距是**半開區間** [Min, Max)，Max 為 null ＝ 無上限。
        /// ⚠ 無條件（None）的流程永遠適用，也是**沒有值可比時的退路** ——
        ///   院方設了條件卻沒給值，寧可走預設流程送出去，也不要讓人送不了單。
        /// ⚠ 命中多條代表級距設壞了（重疊）。存檔時就會擋（ApprovalTierValidator），
        ///   萬一還是發生，取**下限最大**的那一條 —— 較嚴格的那邊，不是查詢順序決定。
        /// </summary>
        public async Task<ApprovalFlow?> PickFlowAsync(int formDefinitionId, ApprovalFacts? facts)
        {
            var flows = await db.ApprovalFlows
                .Where(f => f.FormDefinitionId == formDefinitionId && f.IsActive)
                .OrderBy(f => f.SortOrder).ThenBy(f => f.MinValue)
                .ToListAsync();
            if (flows.Count == 0) return null;

            var conditional = flows.Where(f => f.ConditionKind != ApprovalConditionKind.None).ToList();
            if (conditional.Count == 0) return flows[0];

            var kind = conditional[0].ConditionKind;
            var value = (facts ?? ApprovalFacts.Empty).ValueFor(kind);
            if (value is null)
                return flows.FirstOrDefault(f => f.ConditionKind == ApprovalConditionKind.None)
                       ?? conditional.OrderBy(f => f.MinValue).First();

            return conditional.Where(f => f.Covers(value.Value))
                       .OrderByDescending(f => f.MinValue).FirstOrDefault()
                   ?? flows.FirstOrDefault(f => f.ConditionKind == ApprovalConditionKind.None)
                   ?? conditional.OrderByDescending(f => f.MinValue).First();
        }

        /// <summary>解析後的關卡指派結果（尚未綁到任何單據）</summary>
        public record StepAssignment(
            int StepOrder, int? RoleId, string? RoleName, int? AssignedUserId, string? AssignedUserName,
            /// <summary>⭐ 對造當事人的合意關（不可代理、不可由管理員代簽）</summary>
            bool IsCounterpartConsent = false,
            /// <summary>⭐ 會簽／任一位的名單（2026-10-09）；null ＝ 一般關卡</summary>
            string? CoSignJson = null);

        /// <summary>
        /// 把表單的關卡設定解析成指派結果（不綁單據型別，工作記錄與請假單共用）。
        /// ManagerChain 由送簽者的組織單位往上找主管；找不到則該關退回以角色指派（若有設定）或略過。
        /// </summary>
        /// <param name="picked">
        /// 申請人於送簽時指定的簽核人（關卡序 → 人員 id）。
        /// ⚠ **一定要驗證指定的人在候選名單內** —— 這個值來自前端，
        /// 不驗就等於「申請人可以指定任何人來簽自己的單」，包括自己。
        /// 民生醫院 91 病房要這個功能：副護理長多位、護理長通常一位，申請時挑一位。
        /// </param>
        public async Task<List<StepAssignment>> ResolveAssignmentsAsync(
            int formDefinitionId, int submitterId, IReadOnlyDictionary<int, int>? picked = null,
            ApprovalFacts? facts = null, int? counterpartId = null)
        {
            var flow = await PickFlowAsync(formDefinitionId, facts);
            if (flow is null) return [];

            var flowSteps = await db.FormApprovalSteps
                .Include(s => s.Role)
                .Where(s => s.ApprovalFlowId == flow.Id)
                .OrderBy(s => s.StepOrder)
                .ToListAsync();

            if (flowSteps.Count == 0) return [];

            var levels = await BuildManagerLevelsAsync(submitterId);
            var userNames = await db.Users.ToDictionaryAsync(u => u.Id, u => u.Name);

            var result = new List<StepAssignment>();
            foreach (var s in flowSteps)
            {
                switch (s.StepType)
                {
                    case ApprovalStepType.ManagerChain:
                        var level = Math.Max(1, s.ManagerLevel ?? 1);
                        var candidates = levels.Count >= level ? levels[level - 1] : [];

                        /* ⭐ 2026-10-09：任一位／會簽 —— 這一層有兩位以上主管時，整層都放進名單。
                         * 只有一位時照原本的方式（指派給他），不必多一張名單。 */
                        if (s.SignMode != ApprovalSignMode.Default && candidates.Count > 1)
                        {
                            result.Add(new StepAssignment(s.StepOrder, null, $"第 {level} 層主管", null, null,
                                CoSignJson: NewCoSign(s.SignMode, candidates, userNames)));
                            break;
                        }

                        int? managerId = null;
                        if (candidates.Count > 0)
                        {
                            // 申請人指定了誰？⚠ 必須在候選名單內，否則忽略指定改用預設
                            managerId = picked is not null
                                        && picked.TryGetValue(s.StepOrder, out var want)
                                        && candidates.Contains(want)
                                ? want
                                // 沒指定時取第一位（SortOrder 最前）—— 單一主管的機構行為不變
                                : candidates[0];
                        }

                        if (managerId.HasValue)
                            result.Add(new StepAssignment(s.StepOrder, null, null,
                                managerId, userNames.GetValueOrDefault(managerId.Value)));
                        else if (s.RoleId.HasValue)
                            // 主管鏈不足時退回角色指派（避免單據卡住無人可簽）
                            result.Add(new StepAssignment(s.StepOrder, s.RoleId, s.Role?.Name, null, null));
                        // 無人可指派 → 略過此關
                        break;

                    case ApprovalStepType.Counterpart:
                        // ⚠ 對造是**每張單不同**，設定期填不進去，只能由呼叫端在送簽當下給。
                        //    給不出來就略過這一關 —— 不是所有單據都有對造。
                        if (counterpartId is not int cp) continue;
                        result.Add(new StepAssignment(s.StepOrder, null, null,
                            cp, userNames.GetValueOrDefault(cp), IsCounterpartConsent: true));
                        break;

                    case ApprovalStepType.SpecificUser:
                        if (!s.SpecificUserId.HasValue) continue;
                        result.Add(new StepAssignment(s.StepOrder, null, null,
                            s.SpecificUserId, userNames.GetValueOrDefault(s.SpecificUserId.Value)));
                        break;

                    default:   // Role
                        if (!s.RoleId.HasValue) continue;
                        /* ⭐ 2026-10-09：角色關卡選「會簽」＝**全院**有這個角色的在職人員都要簽（送出當下的名單）。
                         * ⚠ 送單的人自己不列入（自己不簽自己的單）；名單是空的就退回原本的角色關卡，不讓單子卡住。 */
                        if (s.SignMode == ApprovalSignMode.All)
                        {
                            var holders = await db.UserRoles
                                .Where(ur => ur.RoleId == s.RoleId && ur.UserId != submitterId && ur.User!.IsActive)
                                .OrderBy(ur => ur.User!.SortOrder).ThenBy(ur => ur.UserId)
                                .Select(ur => ur.UserId).ToListAsync();
                            if (holders.Count > 0)
                            {
                                result.Add(new StepAssignment(s.StepOrder, null, s.Role?.Name, null, null,
                                    CoSignJson: NewCoSign(s.SignMode, holders, userNames)));
                                break;
                            }
                        }
                        result.Add(new StepAssignment(s.StepOrder, s.RoleId, s.Role?.Name, null, null));
                        break;
                }
            }

            // 連續兩關解析到同一個人時只留前面那關：小型機構的主管鏈常在頂端收斂到院長，
            // 否則同一個人要對同一張單簽兩次。
            // ⚠⚠ 但**合意關不可被去重掉** —— 對造剛好也是排班者時，
            //    「他以排班者身分核准」不等於「他同意自己的班被換掉」，那是兩件事。
            return [.. result.Where((a, i) =>
                i == 0 || a.IsCounterpartConsent || result[i - 1].IsCounterpartConsent
                || a.AssignedUserId is null || a.AssignedUserId != result[i - 1].AssignedUserId)];
        }

        /// <summary>某一關可以由誰簽；<c>Pickable</c> 為 true 時申請人可指定其中一位</summary>
        public record StepChoice(
            int StepOrder, string StepName, bool Pickable,
            int? DefaultUserId, List<StepCandidate> Candidates);

        public record StepCandidate(int UserId, string Name, string? Title, string? UnitName);

        /// <summary>
        /// 送簽前的簽核鏈預覽：這張單會經過哪幾關、每一關有哪些人可簽。
        ///
        /// ⚠ 沒有這一支，「申請時選擇簽核人員」就只是後端能力 ——
        /// 使用者看不到候選人就選不了。本專案一再驗證的那句話：
        /// **後端存在 ≠ 使用者做得到。**
        /// </summary>
        public async Task<List<StepChoice>> PreviewAsync(
            int formDefinitionId, int submitterId, ApprovalFacts? facts = null)
        {
            var flow = await PickFlowAsync(formDefinitionId, facts);
            if (flow is null) return [];
            var flowSteps = await db.FormApprovalSteps
                .Include(s => s.Role)
                .Where(s => s.ApprovalFlowId == flow.Id)
                .OrderBy(s => s.StepOrder)
                .ToListAsync();
            if (flowSteps.Count == 0) return [];

            var levels = await BuildManagerLevelsAsync(submitterId);
            var people = await db.Users
                .Include(u => u.OrgUnit)
                .Where(u => u.IsActive)
                .Select(u => new { u.Id, u.Name, u.Title, UnitName = u.OrgUnit!.Name })
                .ToDictionaryAsync(u => u.Id);

            var result = new List<StepChoice>();
            foreach (var s in flowSteps)
            {
                if (s.StepType != ApprovalStepType.ManagerChain)
                {
                    // 角色關卡與指名關卡沒得選，只顯示名稱
                    result.Add(new StepChoice(s.StepOrder,
                        s.StepType == ApprovalStepType.Role
                            ? (s.Role?.Name ?? "角色關卡") + (s.SignMode == ApprovalSignMode.All ? "（會簽，全部都要簽）" : "")
                            : people.GetValueOrDefault(s.SpecificUserId ?? 0)?.Name ?? "指定人員",
                        false, s.SpecificUserId, []));
                    continue;
                }

                var level = Math.Max(1, s.ManagerLevel ?? 1);
                var ids = levels.Count >= level ? levels[level - 1] : [];
                var cands = ids
                    .Where(people.ContainsKey)
                    .Select(id => new StepCandidate(
                        id, people[id].Name, people[id].Title, people[id].UnitName))
                    .ToList();

                // ⭐ 任一位／會簽：整層都會收到，送單的人不必挑
                var multi = s.SignMode != ApprovalSignMode.Default && cands.Count > 1;
                result.Add(new StepChoice(
                    s.StepOrder,
                    cands.Count == 0 ? "（找不到主管）"
                        : multi ? $"第 {level} 層主管（{(s.SignMode == ApprovalSignMode.All ? "會簽，全部都要簽" : "任一位簽即可")}）"
                        : $"第 {level} 層主管",
                    // 只有一位候選人時不必讓人選 —— 多一個必選欄位只是多一次點擊
                    Pickable: cands.Count > 1 && !multi,
                    DefaultUserId: cands.FirstOrDefault()?.UserId,
                    Candidates: cands));
            }
            return result;
        }

        /// <summary>送簽：把關卡設定解析成工作記錄本輪的快照</summary>
        public async Task<List<WorkRecordApprovalStep>> ResolveStepsAsync(
            int formDefinitionId, int workRecordId, int round, int submitterId,
            IReadOnlyDictionary<int, int>? picked = null, ApprovalFacts? facts = null)
        {
            var assignments = await ResolveAssignmentsAsync(formDefinitionId, submitterId, picked, facts);
            return [.. assignments.Select(a => new WorkRecordApprovalStep
            {
                WorkRecordId = workRecordId,
                Round = round,
                StepOrder = a.StepOrder,
                RoleId = a.RoleId,
                RoleName = a.RoleName,
                AssignedUserId = a.AssignedUserId,
                AssignedUserName = a.AssignedUserName,
                CoSignJson = a.CoSignJson,
            })];
        }

        /// <summary>
        /// 從使用者的主歸屬單位往上收集主管（排除自己），index 0 即直屬主管。
        /// 單位未設主管時自動往上一層找（KMSH 組織模型的 fallback 規則）。
        ///
        /// ⚠ 這一支保留「一層一人」的舊語意，內部改由
        /// <see cref="BuildManagerLevelsAsync"/> 取每層的第一位候選人。
        /// 一層有多位主管時（民生醫院的護理長＋多位副護理長）它只會給你一位，
        /// 需要完整候選名單請改用 <see cref="BuildManagerLevelsAsync"/>。
        /// </summary>
        public async Task<List<int>> BuildManagerChainAsync(int userId) =>
            [.. (await BuildManagerLevelsAsync(userId))
                .Select(lv => lv[0])
                .Distinct()];

        /// <summary>
        /// 往上逐層收集主管，**每一層是一組候選人**。
        ///
        /// ⚠ 為什麼要「一層多人」：民生醫院 91 病房的簽核主管是
        /// 「副護理長**或**護理長」，而副護理長可能有多位 —— 他們是**同一關的候選人**，
        /// 不是兩關。用「一層一人」表達不了，硬用角色關卡表達則會變成全院同職稱都能簽
        /// （51 病房的副護理長簽得掉 91 病房的假單）。
        ///
        /// 來源優先序：該單位在 <c>org_unit_managers</c> 有 Approver 列 → 用那些；
        /// 否則退回 <see cref="OrgUnit.ManagerUserId"/>（育幼院沿用舊資料）。
        /// ⚠ Viewer（督導長）**不列入** —— 他看得到，但不簽核。
        /// </summary>
        public async Task<List<List<int>>> BuildManagerLevelsAsync(int userId)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.OrgUnitId is null) return [];

            var units = await db.OrgUnits
                .Where(o => o.InstitutionId == user.InstitutionId)
                .Select(o => new { o.Id, o.ParentId, o.ManagerUserId })
                .ToDictionaryAsync(o => o.Id);

            var extra = (await db.OrgUnitManagers
                    .Where(m => m.InstitutionId == user.InstitutionId
                                && m.Kind == OrgUnitManagerKind.Approver)
                    .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
                    .Select(m => new { m.OrgUnitId, m.UserId })
                    .ToListAsync())
                .GroupBy(m => m.OrgUnitId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).ToList());

            var levels = new List<List<int>>();
            var seen = new HashSet<int> { userId };   // 自己不簽自己的單
            var cursor = user.OrgUnitId;
            var guard = 0;

            while (cursor.HasValue && guard++ < 100)
            {
                if (!units.TryGetValue(cursor.Value, out var unit)) break;

                // ⚠ 編制上的主管與加簽人員是**聯集，不是取代**。
                //    先前寫成「有 OrgUnitManager 就只用它」，實際後果是：
                //    在編制頁替某單位加一位副主管當簽核人，
                //    原本的主管就靜悄悄地從簽核鏈裡消失 —— 畫面不會有任何異常，
                //    單子只是從此不再送到他手上。
                //    要把某人排除在簽核之外，正確做法是改組織編制的 ManagerUserId，
                //    而不該是「新增另一個人」的副作用。
                //    順序：編制主管在前（預設簽核人不因加簽而改變），加簽者依 SortOrder 接在後。
                //    （2026-08-09 於民生醫院移植後的驗證中發現）
                var candidates = new List<int>();
                if (unit.ManagerUserId is int single) candidates.Add(single);
                if (extra.TryGetValue(unit.Id, out var many))
                    candidates.AddRange(many.Where(id => id != unit.ManagerUserId));

                // ⚠ 去重要跨層做：主管鏈在頂端常收斂到同一個人，
                //    不去重的話同一個人會對同一張單簽兩次
                var fresh = candidates.Where(seen.Add).ToList();
                if (fresh.Count > 0) levels.Add(fresh);

                cursor = unit.ParentId;
            }
            return levels;
        }

        private static string NewCoSign(ApprovalSignMode mode, IEnumerable<int> userIds, Dictionary<int, string> names) =>
            new CoSign
            {
                Mode = mode == ApprovalSignMode.All ? CoSign.ModeAll : CoSign.ModeAny,
                Signers = [.. userIds.Distinct().Select(id => new CoSign.Signer { UserId = id, Name = names.GetValueOrDefault(id) ?? $"#{id}" })],
            }.ToJson();

        /// <summary>
        /// ⭐ 會簽／任一位的關卡：記下這一位的簽核，回傳**這一關是不是已經完成**（2026-10-09）。
        ///
        /// 各單據的同意／退件端點在把關卡設成 Approved／Rejected **之前**呼叫：
        ///   ・一般關卡（沒有名單）→ 回 true，照原本流程。
        ///   ・退件 → 記下是誰退的，回 true（任一人退件＝整張退件，照原本的退件流程）。
        ///   ・任一位同意 → 回 true。
        ///   ・會簽同意 → 全部都簽了才回 true；還有人沒簽就回 false ——
        ///     ⚠ 呼叫端此時**只存檔、不可以**把關卡設成 Approved，也不可以往下一關。
        ///   ・管理員救急（不在名單上、也不是代理人）→ 直接完成這一關，剩下的人標「管理員代簽」。
        /// </summary>
        /// <param name="onBehalfOf">CanAct 回傳的「代誰簽」（代理人代簽時有值）</param>
        public async Task<bool> CoSignActAsync(IApprovalStep step, int actorId, int? onBehalfOf,
                                               ApprovalStepStatus status, string? comment)
        {
            var cs = CoSign.Parse(step.CoSignJson);
            if (cs is null) return true;
            var oldJson = step.CoSignJson!;

            var actorName = await db.Users.Where(u => u.Id == actorId).Select(u => u.Name).FirstOrDefaultAsync();
            var who = onBehalfOf ?? actorId;
            var mine = cs.Signers.FirstOrDefault(x => x.UserId == who && (cs.Mode == CoSign.ModeAny || x.Status == CoSign.Pending));
            var now = DateTime.UtcNow;
            var newStatus = status == ApprovalStepStatus.Rejected ? CoSign.Rejected : CoSign.Approved;

            if (mine is not null)
            {
                mine.Status = newStatus;
                mine.ActedBy = actorId; mine.ActedByName = actorName; mine.ActedAt = now; mine.Comment = comment;
            }
            else
            {
                // 管理員救急：不在名單上 ⇒ 這一關由他處理掉（同意＝剩下的人都標代簽完成；退件照退）
                foreach (var x in cs.Signers.Where(x => x.Status == CoSign.Pending))
                {
                    x.Status = newStatus;
                    x.ActedBy = actorId; x.ActedByName = actorName; x.ActedAt = now;
                    x.Comment = "管理員代簽" + (string.IsNullOrWhiteSpace(comment) ? "" : "：" + comment);
                }
            }
            var newJson = cs.ToJson();

            /* ⚠ 兩個人同時簽（codex 10/9）：名單是一整份 JSON，各自讀、各自寫回，後寫的會蓋掉先寫的。
             *   ⇒ 用「名單還是我讀到的那一份」當條件直接更新；不是的話代表有人剛簽過，請他重新整理再按。
             *   更新成功後告訴 EF 這一欄已經存好了，之後的 SaveChanges 不會再拿舊的版本去蓋。 */
            var et = db.Model.FindEntityType(step.GetType())!;
            var table = et.GetTableName()!;
            var so = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(table, et.GetSchema());
            var col = et.FindProperty(nameof(IApprovalStep.CoSignJson))!.GetColumnName(so)!;
            var idCol = et.FindProperty(nameof(IApprovalStep.Id))!.GetColumnName(so)!;
            var n = await db.Database.ExecuteSqlRawAsync(
                $"UPDATE `{table}` SET `{col}` = {{0}} WHERE `{idCol}` = {{1}} AND `{col}` = {{2}}", newJson, step.Id, oldJson);
            if (n == 0) throw new ApprovalConflictException("剛好有人同時在簽這一關，請重新整理後再按一次。");
            step.CoSignJson = newJson;
            var entry = db.Entry((object)step);
            if (entry.State != EntityState.Detached)
            {
                entry.Property(nameof(IApprovalStep.CoSignJson)).OriginalValue = newJson;
                entry.Property(nameof(IApprovalStep.CoSignJson)).IsModified = false;
            }

            if (status == ApprovalStepStatus.Rejected || cs.Mode == CoSign.ModeAny) return true;
            return cs.Signers.All(x => x.Status == CoSign.Approved);
        }

        /// <summary>
        /// 此人能否簽這一關；可簽時回傳「代誰簽」（自己簽則為 null），
        /// 以及 <paramref name="ViaAdmin"/>＝**是不是靠管理員身分越過關卡**。
        ///
        /// ⭐ 為什麼要多回這一個（業主 2026-09-14 指示）：
        /// 管理員能簽是刻意保留的救急路（主管離職／請長假／組織沒設好，單子會永遠卡住），
        /// 但原本簽完只留下 ActedBy ——
        /// 畫面顯示「關卡：保育組長／同意　系統管理員」，**沒有一個字說明那是代簽**，
        /// 不細看會以為是本人同意的。
        /// ⇒ 呼叫端據此標示為代簽並強制填理由。救急的路還在，紀錄也誠實。
        ///
        /// ⚠ 這裡**只負責判斷**，不決定要不要擋 —— 擋不擋是各單據的政策。
        /// </summary>
        public (bool CanAct, int? OnBehalfOf, bool ViaAdmin) CanAct(IApprovalStep step, ApprovalContext ctx)
        {
            /* ⭐ 會簽／任一位（2026-10-09）：看名單上**還能簽**的人。
             * 會簽已經簽過的人不在 Open 裡 ⇒ 他的待簽清單不會再出現這張單。 */
            if (CoSign.Parse(step.CoSignJson) is { } cs)
            {
                var open = cs.Open.Select(x => x.UserId).ToList();
                if (open.Contains(ctx.UserId)) return (true, null, false);
                var principal = open.FirstOrDefault(ctx.PrincipalIds.Contains);
                if (principal != 0) return (true, principal, false);
                return ctx.IsAdmin ? (true, null, true) : (false, null, false);
            }

            if (step.AssignedUserId.HasValue)
            {
                if (step.AssignedUserId.Value == ctx.UserId) return (true, null, false);
                if (ctx.PrincipalIds.Contains(step.AssignedUserId.Value)) return (true, step.AssignedUserId, false);
                // ⚠ 管理員代簽：代的就是這一關原本指派的那個人，呼叫端據此填 OnBehalfOf
                return ctx.IsAdmin ? (true, step.AssignedUserId, true) : (false, null, false);
            }

            if (step.RoleId.HasValue)
            {
                if (ctx.OwnRoleIds.Contains(step.RoleId.Value)) return (true, null, false);
                if (ctx.RoleViaPrincipal.TryGetValue(step.RoleId.Value, out var principal)) return (true, principal, false);
                // ⚠ 角色關卡沒有特定的人可代 ⇒ OnBehalfOf 留 null，只標示為管理員代簽
                return ctx.IsAdmin ? (true, null, true) : (false, null, false);
            }

            return ctx.IsAdmin ? (true, null, true) : (false, null, false);
        }
    }
}

namespace Lobsang.Services
{
    /// <summary>會簽名單剛被別人改過（兩個人同時簽）⇒ 回 409 請重新整理</summary>
    public class ApprovalConflictException(string message) : Exception(message);
}
