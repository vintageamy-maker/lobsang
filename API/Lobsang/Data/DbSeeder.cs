using Microsoft.EntityFrameworkCore;
using Lobsang.Models;

namespace Lobsang.Data
{
    /// <summary>
    /// 初始資料：權限字典、機構、標準角色、選單、示範組織與表單、系統管理員。
    ///
    /// ⭐ 規則（全部源自 SmartCare 實際踩過的坑）：
    /// <list type="bullet">
    /// <item>**可重複執行**：每一段都先查「有沒有」，有就略過。每次啟動都會跑。</item>
    /// <item>**只新增，不改既有資料**：客戶自己改過的選單、角色、權限，種子不可蓋回去 ——
    ///   那種覆蓋在畫面上看不出來，只是某一項「跑掉了」。</item>
    /// <item>**角色用代碼找，不用名稱**（見 <see cref="RoleCodes"/>）。</item>
    /// <item>**授權只給一次**（<see cref="GrantOnceAsync"/>）：客戶拿掉的權限，重啟不會長回來。</item>
    /// <item>**選單以路徑判斷有沒有**：客戶改了名稱或搬了位置，也不會再長一份。</item>
    /// </list>
    /// </summary>
    public static class DbSeeder
    {
        /// <summary>
        /// 權限字典。⭐ 新專案的模組在這裡追加權限碼（系統管理員會自動拿到全部）。
        /// ⚠ 權限碼一旦上線就不要改字 —— 角色設定、選單、程式都靠它對上。
        /// </summary>
        private static readonly (string Code, string Name, string Group)[] PermissionSeed =
        [
            ("system.admin",      "系統管理",     "系統"),
            ("user.manage",       "帳號管理",     "系統"),
            ("role.manage",       "角色管理",     "系統"),
            ("menu.manage",       "選單管理",     "系統"),
            ("org.manage",        "組織編制維護", "系統"),
            ("audit.read",        "異動紀錄查詢", "系統"),
            ("errorlog.read",     "錯誤日誌查詢", "系統"),
            ("delegation.manage", "代理設定維護", "系統"),
            // ⚠ 簽核流程決定「誰有權簽誰的單」，等同於權限設定 —— 刻意獨立成一個權限碼
            ("approval.config",   "簽核流程設定", "系統"),
            ("record.read",       "表單查詢",     "表單"),
            ("record.write",      "表單填寫",     "表單"),
            // 撤銷已完成的表單屬主管職權，與填寫權分離
            ("record.cancel",     "表單撤銷",     "表單"),
            // Lobsang：書單（2026-11-07）。查書本身是公開的，這個權限管「書單的新增、修改、刪除」
            ("book.manage",       "書單維護",     "書籍"),
            // 2026-11-08：採購單的核准／退回。業主決定先只給系統管理員（系統管理員自動擁有所有權限）
            ("purchase.approve",  "採購審核",     "書籍"),
        ];

        /// <param name="seedDemoUsers">
        /// 是否植入示範帳號（manager01／staff01，密碼寫死且不強制變更）。
        /// ⚠⚠ **正式環境一律傳 false**：出現在正式站就是一組已知密碼的後門。
        /// ⚠ SmartCare 2026-09-13 實際出事：帳號刪了，隔天部署重啟又被整批種回來 ——
        ///   會自動重建的東西必須連來源一起關掉。
        /// </param>
        public static async Task SeedAsync(AppDbContext db, bool seedDemoUsers = true)
        {
            // ===== 權限字典 =====
            foreach (var (code, name, group) in PermissionSeed)
            {
                if (!await db.Permissions.AnyAsync(p => p.Code == code))
                    db.Permissions.Add(new Permission { Code = code, Name = name, Group = group });
            }
            await db.SaveChangesAsync();

            // ===== 機構 =====
            // ⭐ 新專案改這裡的名稱（之後在資料庫改也可以，種子不會蓋回去）
            var institution = await db.Institutions.FirstOrDefaultAsync();
            if (institution is null)
            {
                // ⚠ Lobsang 目前只在本機跑，檔案先存本機（Storage:Local:Root）。
                //   上開發站改用 GCS（bucket：stage-lobsang）時，要把資料庫 institutions.storage_backend 改成 Gcs ——
                //   種子只在機構不存在時建立一次，改這裡不會影響已建好的資料庫。
                institution = new Institution { Name = "Lobsang", ShortName = "Lobsang", StorageBackend = StorageBackend.Local };
                db.Institutions.Add(institution);
                await db.SaveChangesAsync();
            }
            var inst = institution.Id;

            // ===== 標準角色 =====
            var adminRole = await EnsureRoleAsync(db, inst, RoleCodes.SysAdmin, "系統管理員", "全功能管理權限");
            var managerRole = await EnsureRoleAsync(db, inst, RoleCodes.Manager, "主管", "可簽核、可撤銷表單");
            var staffRole = await EnsureRoleAsync(db, inst, RoleCodes.Staff, "一般同仁", "填寫與查詢表單");

            // 系統管理員永遠擁有全部權限：新增權限碼時自動補齊
            var adminHeld = await db.RolePermissions.Where(rp => rp.RoleId == adminRole.Id)
                .Select(rp => rp.PermissionId).ToListAsync();
            var adminMissing = await db.Permissions.Where(p => !adminHeld.Contains(p.Id))
                .Select(p => p.Id).ToListAsync();
            if (adminMissing.Count > 0)
            {
                db.RolePermissions.AddRange(adminMissing.Select(pid => new RolePermission { RoleId = adminRole.Id, PermissionId = pid }));
                await db.SaveChangesAsync();
            }

            await GrantOnceAsync(db, inst, [managerRole.Id], ["record.read", "record.write", "record.cancel"]);
            await GrantOnceAsync(db, inst, [staffRole.Id], ["record.read", "record.write"]);

            // ===== 選單 =====
            // ⚠ 以路徑判斷有沒有；目錄以名稱＋無上層判斷。客戶改過的不動。
            var formDir = await EnsureMenuDirAsync(db, "表單", "form", 20);
            var orgDir = await EnsureMenuDirAsync(db, "組織人事", "apartment", 30);
            var sysDir = await EnsureMenuDirAsync(db, "系統管理", "setting", 90);
            var bookDir = await EnsureMenuDirAsync(db, "書籍", "book", 10);

            await EnsureMenuAsync(db, formDir, "表單列表", "/records", "file-text", 1, "record.read");
            // 2026-11-08 業主：「書單」就是採購候選，選單改名「採購候選」；另加「商品」（實際販售，目前是示意版 mock）
            await EnsureMenuAsync(db, bookDir, "採購候選", "/books", "book", 1, "book.manage");
            await RenameDefaultMenuAsync(db, "/books", "書單", "採購候選");
            await EnsureMenuAsync(db, bookDir, "商品清單", "/products", "shop", 2, "book.manage");
            await RenameDefaultMenuAsync(db, "/products", "商品", "商品清單");   // 2026-11-08 業主：選單「商品」改叫「商品清單」
            await EnsureMenuAsync(db, orgDir, "組織編制", "/org-units", "apartment", 1, "org.manage");
            await EnsureMenuAsync(db, orgDir, "代理設定", "/delegations", "solution", 2, "delegation.manage");
            await EnsureMenuAsync(db, orgDir, "簽核流程", "/approval-flows", "branches", 3, "approval.config");
            await EnsureMenuAsync(db, sysDir, "帳號管理", "/users", "idcard", 1, "user.manage");
            await EnsureMenuAsync(db, sysDir, "角色管理", "/roles", "safety", 2, "role.manage");
            await EnsureMenuAsync(db, sysDir, "選單管理", "/menus", "menu", 3, "menu.manage");
            await EnsureMenuAsync(db, sysDir, "異動紀錄", "/audit-logs", "audit", 4, "audit.read");
            await EnsureMenuAsync(db, sysDir, "錯誤日誌", "/error-logs", "bell", 5, "errorlog.read");
            // 每個人設定自己的認證密碼（二次密碼）。⚠ 權限碼留空＝登入者都看得到（設定的是自己的）
            await EnsureMenuAsync(db, sysDir, "我的認證密碼", "/my/step-up-password", "lock", 6, null);

            // ===== 示範組織 =====
            var hq = await EnsureUnitAsync(db, inst, null, "HQ", "本部", "機構", 1);
            var adm = await EnsureUnitAsync(db, inst, hq.Id, "ADM", "行政部", "部門", 1);

            // ===== 示範表單＋簽核流程 =====
            // ⭐ 讓「填寫 → 送簽 → 主管簽核 → 完成／退件」整條路一開始就能走。
            //   新專案的表單照這個格式加（SchemaJson 的欄位型別見前端 formSchema.ts）。
            var form = await db.FormDefinitions.FirstOrDefaultAsync(f => f.InstitutionId == inst && f.Code == "general-request");
            if (form is null)
            {
                form = new FormDefinition
                {
                    InstitutionId = inst, Code = "general-request", Name = "通用申請單", Category = "一般",
                    Frequency = FormFrequency.Adhoc, SortOrder = 1,
                    SchemaJson = """
                    {
                      "signatures": ["申請人", "直屬主管"],
                      "sections": [
                        { "title": "申請內容", "fields": [
                          { "key": "subject", "label": "主旨", "type": "text", "required": true },
                          { "key": "kind", "label": "類別", "type": "select", "options": ["一般", "採購", "其他"] },
                          { "key": "amount", "label": "金額（元）", "type": "number" },
                          { "key": "content", "label": "說明", "type": "textarea", "required": true }
                        ]}
                      ]
                    }
                    """,
                };
                db.FormDefinitions.Add(form);
                await db.SaveChangesAsync();
            }
            var flow = await DefaultFlowAsync(db, inst, form.Id);
            if (!await db.FormApprovalSteps.AnyAsync(s => s.ApprovalFlowId == flow.Id))
            {
                // 第 1 關：申請人的直屬主管（從申請人的單位往上找）
                db.FormApprovalSteps.Add(new FormApprovalStep
                {
                    ApprovalFlowId = flow.Id, StepOrder = 1,
                    StepType = ApprovalStepType.ManagerChain, ManagerLevel = 1,
                });
                await db.SaveChangesAsync();
            }

            // ===== 系統管理員帳號 =====
            if (!await db.Users.AnyAsync(u => u.Account == "admin"))
            {
                var admin = new User
                {
                    InstitutionId = inst,
                    Account = "admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Lobsang#2026"),
                    Name = "系統管理員",
                    // ⚠ 正式站第一次登入一定要改密碼；開發站要讓快速登入直接進得去
                    MustChangePassword = !seedDemoUsers,
                };
                db.Users.Add(admin);
                await db.SaveChangesAsync();
                db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole.Id });
                await db.SaveChangesAsync();
            }

            // ===== 示範帳號（只有非正式環境）=====
            if (seedDemoUsers)
            {
                var manager = await EnsureDemoUserAsync(db, inst, "manager01", "王主管", managerRole.Id, adm.Id, "部門主管", "EMP-001", 1);
                await EnsureDemoUserAsync(db, inst, "staff01", "陳同仁", staffRole.Id, adm.Id, "專員", "EMP-002", 2);
                // 行政部的主管 ＝ 王主管（陳同仁送簽 → 王主管簽）。⚠ 只在還沒設時才設
                if (adm.ManagerUserId is null)
                {
                    adm.ManagerUserId = manager.Id;
                    await db.SaveChangesAsync();
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        // 小工具
        // ════════════════════════════════════════════════════════════

        /// <summary>用代碼找標準角色；沒有就建立（名稱只在建立時用，之後客戶怎麼改名都找得到）</summary>
        private static async Task<Role> EnsureRoleAsync(AppDbContext db, int institutionId, string code, string name, string description)
        {
            var role = await RoleByCodeAsync(db, institutionId, code);
            if (role is not null) return role;
            role = new Role { InstitutionId = institutionId, Code = code, Name = name, Description = description };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            return role;
        }

        internal static Task<Role?> RoleByCodeAsync(AppDbContext db, int institutionId, string code) =>
            db.Roles.FirstOrDefaultAsync(r => r.InstitutionId == institutionId && r.Code == code);

        /// <summary>內部角色（排除外部角色；客戶自建、沒有代碼的角色算內部）</summary>
        internal static Task<List<int>> InternalRoleIdsAsync(AppDbContext db, int institutionId) =>
            db.Roles.Where(r => r.InstitutionId == institutionId
                                && (r.Code == null || !RoleCodes.External.Contains(r.Code)))
                .Select(r => r.Id).ToListAsync();

        /// <summary>
        /// ⭐ 種子授權「只做一次」：每個（角色, 權限）配對只給一次，留下 <see cref="SeedMarker"/>。
        /// ⚠ 給過之後客戶在角色管理拿掉，重啟**不會**再補回。
        /// </summary>
        internal static async Task GrantOnceAsync(AppDbContext db, int institutionId,
                                                  IEnumerable<int> roleIds, IEnumerable<string> permCodes)
        {
            var roles = roleIds.Distinct().ToList();
            var codes = permCodes.Distinct().ToList();
            if (roles.Count == 0 || codes.Count == 0) return;

            var perms = await db.Permissions.Where(p => codes.Contains(p.Code))
                .Select(p => new { p.Id, p.Code }).ToListAsync();
            var done = (await db.SeedMarkers
                    .Where(m => m.InstitutionId == institutionId && m.Key.StartsWith("grant:"))
                    .Select(m => m.Key).ToListAsync())
                .ToHashSet();
            var held = (await db.RolePermissions.Where(rp => roles.Contains(rp.RoleId))
                    .Select(rp => new { rp.RoleId, rp.PermissionId }).ToListAsync())
                .Select(x => (x.RoleId, x.PermissionId)).ToHashSet();

            var now = DateTime.UtcNow;
            var changed = false;
            foreach (var rid in roles)
                foreach (var p in perms)
                {
                    var key = $"grant:{rid}:{p.Code}";
                    if (done.Contains(key)) continue;   // ⚠ 給過了：客戶拿掉就是拿掉
                    if (!held.Contains((rid, p.Id)))
                        db.RolePermissions.Add(new RolePermission { RoleId = rid, PermissionId = p.Id });
                    db.SeedMarkers.Add(new SeedMarker { InstitutionId = institutionId, Key = key, CreatedAt = now });
                    done.Add(key);
                    changed = true;
                }
            if (changed) await db.SaveChangesAsync();
        }

        private static async Task<Menu> EnsureMenuDirAsync(AppDbContext db, string name, string icon, int sortOrder)
        {
            var dir = await db.Menus.FirstOrDefaultAsync(m => m.Name == name && m.ParentId == null && m.Path == null);
            if (dir is not null) return dir;
            dir = new Menu { Name = name, Icon = icon, SortOrder = sortOrder };
            db.Menus.Add(dir);
            await db.SaveChangesAsync();
            return dir;
        }

        private static async Task EnsureMenuAsync(AppDbContext db, Menu parent, string name, string path,
                                                  string icon, int sortOrder, string? permissionCode)
        {
            if (await db.Menus.AnyAsync(m => m.Path == path)) return;
            db.Menus.Add(new Menu
            {
                ParentId = parent.Id, Name = name, Path = path, Icon = icon,
                SortOrder = sortOrder, PermissionCode = permissionCode,
            });
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// 預設選單改名：只在名稱還是「舊的預設值」時才改（客戶自己在選單管理改過的不動）。
        /// 例：/books 從「書單」改成「採購候選」（2026-11-08）。
        /// </summary>
        private static async Task RenameDefaultMenuAsync(AppDbContext db, string path, string oldName, string newName)
        {
            var m = await db.Menus.FirstOrDefaultAsync(x => x.Path == path && x.Name == oldName);
            if (m is null) return;
            m.Name = newName;
            await db.SaveChangesAsync();
        }

        private static async Task<OrgUnit> EnsureUnitAsync(AppDbContext db, int institutionId, int? parentId,
                                                           string code, string name, string kind, int sortOrder)
        {
            var unit = await db.OrgUnits.FirstOrDefaultAsync(o => o.InstitutionId == institutionId && o.Code == code);
            if (unit is not null) return unit;
            unit = new OrgUnit
            {
                InstitutionId = institutionId, ParentId = parentId, Code = code, Name = name,
                Kind = kind, SortOrder = sortOrder,
            };
            db.OrgUnits.Add(unit);
            await db.SaveChangesAsync();
            return unit;
        }

        private static async Task<User> EnsureDemoUserAsync(AppDbContext db, int institutionId, string account,
            string name, int roleId, int orgUnitId, string title, string employeeNo, int sortOrder)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Account == account);
            if (user is not null) return user;
            user = new User
            {
                InstitutionId = institutionId, Account = account, Name = name,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test#2026"),
                OrgUnitId = orgUnitId, Title = title, EmployeeNo = employeeNo, SortOrder = sortOrder,
                MustChangePassword = false,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
            await db.SaveChangesAsync();
            return user;
        }

        /// <summary>
        /// 建一條「無條件」的預設流程給某張表單。
        /// ⚠ 關卡不直接掛表單，一定要先有流程（流程可依金額／天數分流，見 ApprovalFlow）。
        /// </summary>
        private static async Task<ApprovalFlow> DefaultFlowAsync(AppDbContext db, int institutionId, int formDefinitionId)
        {
            var existing = await db.ApprovalFlows.FirstOrDefaultAsync(f => f.FormDefinitionId == formDefinitionId);
            if (existing is not null) return existing;

            var flow = new ApprovalFlow
            {
                InstitutionId = institutionId,
                FormDefinitionId = formDefinitionId,
                ConditionKind = ApprovalConditionKind.None,
                MinValue = 0, MaxValue = null,
                Name = "預設流程", SortOrder = 1, IsActive = true,
            };
            db.ApprovalFlows.Add(flow);
            await db.SaveChangesAsync();
            return flow;
        }
    }
}
