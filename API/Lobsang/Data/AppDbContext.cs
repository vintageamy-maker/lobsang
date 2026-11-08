using Microsoft.EntityFrameworkCore;
using Lobsang.Models;

namespace Lobsang.Data
{
    // 命名慣例：UseSnakeCaseNamingConvention 自動將實體/屬性轉為複數前的 snake_case 欄位名，
    // 資料表名以 ToTable 明確指定複數 snake_case（沿用 meekee 慣例）。
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Institution> Institutions => Set<Institution>();
        public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
        public DbSet<UserDelegation> UserDelegations => Set<UserDelegation>();
        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Menu> Menus => Set<Menu>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();
        public DbSet<FormDefinition> FormDefinitions => Set<FormDefinition>();
        public DbSet<WorkRecord> WorkRecords => Set<WorkRecord>();
        public DbSet<WorkRecordTag> WorkRecordTags => Set<WorkRecordTag>();
        public DbSet<ApprovalFlow> ApprovalFlows => Set<ApprovalFlow>();
        public DbSet<FormApprovalStep> FormApprovalSteps => Set<FormApprovalStep>();
        public DbSet<WorkRecordApprovalStep> WorkRecordApprovalSteps => Set<WorkRecordApprovalStep>();
        // ⚠ 被拒絕的打卡嘗試。**與 AttendancePunches 是不同的表**，理由見 PunchRejection 檔頭
        public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
        public DbSet<OrgUnitManager> OrgUnitManagers => Set<OrgUnitManager>();
        public DbSet<StepUpSession> StepUpSessions => Set<StepUpSession>();
        public DbSet<LineWebhookEvent> LineWebhookEvents => Set<LineWebhookEvent>();
        public DbSet<LineBindLog> LineBindLogs => Set<LineBindLog>();
        // 設施列管與合約（2026-09-15）。⚠ 這裡**沒有**「設施」表 —— 東西的身分留在 Assets
        // 膳食廚務：菜單與出餐（2026-09-16）。⚠ 冷藏溫度**不在這裡** —— 在每日檢核的 kitchen-temp
        public DbSet<SeedMarker> SeedMarkers => Set<SeedMarker>();
        // Lobsang：書單（2026-11-07）
        public DbSet<Book> Books => Set<Book>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
        public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Institution>(e =>
            {
                e.ToTable("institutions");
            });

            modelBuilder.Entity<Book>(e =>
            {
                e.ToTable("books");
                e.HasIndex(b => new { b.InstitutionId, b.Code }).IsUnique();
                e.HasIndex(b => b.Title);
                e.Property(b => b.DataJson).HasColumnType("longtext");
            });

            modelBuilder.Entity<Product>(e =>
            {
                e.ToTable("products");
                e.HasIndex(p => new { p.InstitutionId, p.Sku }).IsUnique();
                e.HasIndex(p => p.Code);
                e.HasIndex(p => p.Name);
                e.Property(p => p.DataJson).HasColumnType("longtext");
            });

            modelBuilder.Entity<PurchaseOrder>(e =>
            {
                e.ToTable("purchase_orders");
                e.HasIndex(o => new { o.InstitutionId, o.OrderNo }).IsUnique();
                e.HasMany(o => o.Items).WithOne(i => i.PurchaseOrder).HasForeignKey(i => i.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PurchaseOrderItem>(e =>
            {
                e.ToTable("purchase_order_items");
                e.HasIndex(i => i.Status);
                e.HasIndex(i => i.Code);
                e.Property(i => i.DataJson).HasColumnType("longtext");
            });

            modelBuilder.Entity<OrgUnit>(e =>
            {
                e.ToTable("org_units");
                e.HasIndex(o => new { o.InstitutionId, o.Code }).IsUnique();
                e.HasOne(o => o.Parent).WithMany().HasForeignKey(o => o.ParentId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<User>(e =>
            {
                e.ToTable("users");
                e.HasIndex(u => u.Account).IsUnique();
                /*
                 * ⭐ 一個 LINE 帳號只能綁一個人 —— 資料庫層擋，不靠程式記得檢查。
                 * ⚠ 允許多個 NULL 是必要的：絕大多數人沒綁定，
                 *   MySQL 的 UNIQUE 對 NULL 不做唯一性檢查，正好是我們要的。
                 */
                e.HasIndex(u => u.LineUserId).IsUnique();
                // 只索引 EmployeeNo 單欄：複合索引 (institution_id, employee_no) 會讓 EF
                // 認為 FK 用的 ix_users_institution_id 冗餘而嘗試移除，MySQL 會拒絕
                e.HasIndex(u => u.EmployeeNo);
                e.HasOne(u => u.Institution).WithMany().HasForeignKey(u => u.InstitutionId);
                e.HasOne(u => u.OrgUnit).WithMany().HasForeignKey(u => u.OrgUnitId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<UserDelegation>(e =>
            {
                e.ToTable("user_delegations");
                e.HasIndex(d => new { d.PrincipalUserId, d.StartDate, d.EndDate });
                e.HasIndex(d => d.DelegateUserId);
                e.HasOne(d => d.Principal).WithMany().HasForeignKey(d => d.PrincipalUserId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(d => d.Delegate).WithMany().HasForeignKey(d => d.DelegateUserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Role>(e =>
            {
                e.ToTable("roles");
                e.HasIndex(r => new { r.InstitutionId, r.Name }).IsUnique();
                // 代碼（2026-10-09）：同機構不可重複；院方自建的角色是 null（MySQL 允許多個 NULL）
                e.HasIndex(r => new { r.InstitutionId, r.Code }).IsUnique();
            });

            modelBuilder.Entity<UserRole>(e =>
            {
                e.ToTable("user_roles");
                e.HasKey(x => new { x.UserId, x.RoleId });
                e.HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Role).WithMany(r => r.UserRoles).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Permission>(e =>
            {
                e.ToTable("permissions");
                e.HasIndex(p => p.Code).IsUnique();
            });

            modelBuilder.Entity<RolePermission>(e =>
            {
                e.ToTable("role_permissions");
                e.HasKey(x => new { x.RoleId, x.PermissionId });
                e.HasOne(x => x.Role).WithMany(r => r.RolePermissions).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
            });

            // ⚠ small_families 已停用（2026-08-23），保留對映只為了讓舊表還查得到
#pragma warning disable CS0618
#pragma warning restore CS0618





















































            /* 社工收案入住表單（2026-09-08）——「一個院童、一張表、一次安置」為一筆。
             * 8 張共用一張表，內容放 JSON（理由見 Models/SocialIntakeForm.cs 的類別註解）。 */

            /* 每日檢核（2026-09-07）——「一張表、一天、一個範圍」為一筆。
             * ⚠⚠ 即使紙本是「一個月一張」，儲存仍是一天一筆；月表在匯出時聚合。
             *   理由見 Models/DailyCheck.cs 的類別註解。 */
            // 員工體溫（2026-10-07）：一人一天一筆
            // 學期（2026-10-08，社7 學校生活適應調查表）：一個機構、一學年、一學期一筆
            // 種子做過的記號（2026-10-09）：每一件只做一次，院方拿掉的不再補回
            modelBuilder.Entity<SeedMarker>(e =>
            {
                e.ToTable("seed_markers");
                e.HasIndex(x => new { x.InstitutionId, x.Key }).IsUnique();
            });
            // 組織異動單（2026-10-09）



            /* 療育／諮商／復健的季度設定。
             * ⭐ 與 IspPlan／IspGoal 刻意同構（院方 2026-09-02：「比照 ISP 的做法」），
             *   索引、外鍵、query filter 的取捨也一併沿用 —— 兩邊行為一致比較好維護。 */


            modelBuilder.Entity<Menu>(e =>
            {
                e.ToTable("menus");
                e.HasOne(m => m.Parent).WithMany().HasForeignKey(m => m.ParentId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<AuditLog>(e =>
            {
                e.ToTable("audit_logs");
                e.Property(a => a.BeforeJson).HasColumnType("text");
                e.Property(a => a.AfterJson).HasColumnType("text");
                e.HasIndex(a => new { a.InstitutionId, a.CreatedAt });
                e.HasIndex(a => new { a.TargetType, a.TargetId });
            });

            modelBuilder.Entity<FormDefinition>(e =>
            {
                e.ToTable("form_definitions");
                e.HasIndex(f => new { f.InstitutionId, f.Code }).IsUnique();
                e.Property(f => f.SchemaJson).HasColumnType("json");
                e.Property(f => f.Frequency).HasConversion<string>().HasMaxLength(20);
            });

            modelBuilder.Entity<WorkRecord>(e =>
            {
                e.ToTable("work_records");
                e.HasIndex(r => new { r.InstitutionId, r.RecordDate });
                e.HasIndex(r => new { r.FormDefinitionId, r.RecordDate });
                e.Property(r => r.ContentJson).HasColumnType("json");
                e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
                e.HasOne(r => r.FormDefinition).WithMany().HasForeignKey(r => r.FormDefinitionId);
                e.HasQueryFilter(r => r.DeletedAt == null);
            });


            modelBuilder.Entity<ErrorLog>(e =>
            {
                e.ToTable("error_logs");
                e.Property(x => x.StackTrace).HasColumnType("text");
                e.HasIndex(x => x.CreatedAt);
                e.HasIndex(x => new { x.InstitutionId, x.StatusCode });
            });






            // ===== 工時制度層（v9）=====






            // ===== 班表確認層（v10）=====













            // 志工（2026-10-01，院方志願服務管理辦法與表單）

            // 車輛（2026-10-01 示意頁改真資料）






            // ===== 教育訓練（2026-09-15）=====




            // ===== 設施列管與合約（2026-09-15）=====
            // ⚠⚠ 這一區**沒有「設施」表** —— 東西的身分留在 assets，
            //    這裡只存「對那東西的義務」。見 Models/Facility.cs 檔頭的鐵律。








            // ===== 膳食廚務：菜單與出餐（2026-09-16）=====
            // ⚠ 兩個入口共用這一份資料：廚師在「菜單與出餐」登打、營養師在「菜單清單」回意見。
            //    權限的切割在 Controller（kitchen.menu 寫菜單、kitchen.nutrition 寫意見），不在這裡。












            modelBuilder.Entity<ApprovalFlow>(e =>
            {
                e.ToTable("approval_flows");
                e.HasIndex(x => new { x.InstitutionId, x.FormDefinitionId, x.SortOrder });
                e.Property(x => x.ConditionKind).HasConversion<string>().HasMaxLength(20);
                e.HasOne(x => x.FormDefinition).WithMany().HasForeignKey(x => x.FormDefinitionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<FormApprovalStep>(e =>
            {
                e.ToTable("form_approval_steps");
                // ⚠ 唯一索引跟著換到 flow —— 兩條流程各有自己的第 1 關，掛在表單上會撞
                e.HasIndex(x => new { x.ApprovalFlowId, x.StepOrder }).IsUnique();
                e.Property(x => x.StepType).HasConversion<string>().HasMaxLength(20);
                e.HasOne(x => x.ApprovalFlow).WithMany(f => f.Steps).HasForeignKey(x => x.ApprovalFlowId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<WorkRecordApprovalStep>(e =>
            {
                e.ToTable("work_record_approval_steps");
                e.HasIndex(x => new { x.WorkRecordId, x.Round, x.StepOrder }).IsUnique();
                e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
                e.HasOne(x => x.WorkRecord).WithMany(r => r.ApprovalSteps).HasForeignKey(x => x.WorkRecordId).OnDelete(DeleteBehavior.Cascade);
                e.HasQueryFilter(x => x.WorkRecord!.DeletedAt == null);
            });

            modelBuilder.Entity<WorkRecordTag>(e =>
            {
                e.ToTable("work_record_tags");
                e.HasKey(x => new { x.WorkRecordId, x.Tag });
                e.HasOne(x => x.WorkRecord).WithMany(r => r.Tags).HasForeignKey(x => x.WorkRecordId).OnDelete(DeleteBehavior.Cascade);
                e.HasQueryFilter(x => x.WorkRecord!.DeletedAt == null);
                e.HasIndex(x => x.Tag);
            });















            modelBuilder.Entity<StoredFile>(e =>
            {
                e.ToTable("stored_files");
                // 掛在單據上的附件清單：這是最主要的查詢
                e.HasIndex(x => new { x.InstitutionId, x.OwnerType, x.OwnerId });
                // 重複上傳偵測
                e.HasIndex(x => new { x.InstitutionId, x.Sha256 });
                // 物件路徑必須唯一 —— 撞鍵代表 GUID 重複或程式有誤，要炸出來而不是覆蓋
                e.HasIndex(x => x.ObjectKey).IsUnique();
                e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
                e.Property(x => x.Backend).HasConversion<string>().HasMaxLength(20);
                e.HasQueryFilter(x => x.DeletedAt == null);
            });

            modelBuilder.Entity<Institution>(e =>
            {
                e.Property(x => x.StorageBackend).HasConversion<string>().HasMaxLength(20);
            });

            modelBuilder.Entity<OrgUnitManager>(e =>
            {
                e.ToTable("org_unit_managers");
                // 同一人對同一單位只能有一種身分 —— 同時是 Approver 又是 Viewer 沒有意義，
                // 而且會讓「要不要進簽核鏈」變成不確定
                e.HasIndex(x => new { x.OrgUnitId, x.UserId }).IsUnique();
                e.HasIndex(x => new { x.InstitutionId, x.UserId });
                e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
                e.HasOne(x => x.OrgUnit).WithMany().HasForeignKey(x => x.OrgUnitId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });


            // ── 法定費率 ─────────────────────────────────────────────
            // 只存 EffectiveStart 不存結束日：存結束日會使「匯入下一年度」變成 UPDATE，
            // 違反「法規只能新增」。查詢改以「起始日 <= 查詢日之中最大者」定位版本。
            // 附帶好處是來源的資料缺口（勞保 2016 全年無版本、職災 2022-05-01 前不存在）
            // 會自然落回前一版或回空，不必寫特例。









            // ── 薪資科目三層 ──────────────────────────────────────────
            // L3 結構 → L2 科目 → L1 會計科目，單向依賴。
            // 反向引用（誰用了我）一律用查詢算，不存欄位 ——
            // 存了就要在每次增刪改時同步維護。






            // ── 出勤異常與全勤獎金規則 ──────────────────────────────






            // ── 員工薪資與投保資料 ──────────────────────────────────
            // ⚠ 都要效期化：算 6 月的薪水要用 6 月當時的金額，不是現在的。






            modelBuilder.Entity<StepUpSession>(e =>
            {
                e.ToTable("step_up_sessions");
                e.Property(x => x.Token).HasMaxLength(64);
                e.Property(x => x.UserAgent).HasMaxLength(256);
                e.Property(x => x.IpAddress).HasMaxLength(64);
                // 每次受保護的請求都會用 (user_id, token) 查一次，要走索引
                e.HasIndex(x => new { x.UserId, x.Token });
                e.HasOne(x => x.User).WithMany()
                    .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            });





            modelBuilder.Entity<LineBindLog>(e =>
            {
                e.ToTable("line_bind_logs");
                e.Property(x => x.Action).HasConversion<string>().HasMaxLength(8);
                e.HasIndex(x => new { x.UserId, x.ActedAt });
                e.HasIndex(x => x.LineUserId);
                // ⚠ 使用者刪除時**不連帶刪軌跡**（Restrict）—— 綁定紀錄是身分變更的證據
                e.HasOne(x => x.User).WithMany()
                    .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<LineWebhookEvent>(e =>
            {
                e.ToTable("line_webhook_events");
                e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
                /*
                 * ⭐ 去重的最後一道防線。LINE 沒收到 200 就重送，重送時 webhookEventId 不變。
                 * ⚠ 允許多個 NULL 是**刻意的**：驗證請求與舊事件型別沒有這個欄位，
                 *   MySQL 的 UNIQUE 對 NULL 不做唯一性檢查，正好是我們要的行為。
                 */
                e.HasIndex(x => x.WebhookEventId).IsUnique();
                // 查詢一律是「最近收到的」
                e.HasIndex(x => x.ReceivedAt);
                e.HasIndex(x => x.LineUserId);
                // 背景補做時要撈還沒處理的
                e.HasIndex(x => x.Status);
            });




        }
    }
}
