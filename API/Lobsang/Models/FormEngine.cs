using System.ComponentModel.DataAnnotations;

namespace Lobsang.Models
{
    /// <summary>表單填寫頻率（供期限引擎產生待辦；Adhoc＝不定期）</summary>
    public enum FormFrequency { Daily, Weekly, Monthly, Quarterly, Adhoc }

    public enum RecordStatus
    {
        /// <summary>草稿（可編輯/可刪除；被退件或抽單亦回到此狀態）</summary>
        Draft,
        /// <summary>簽核中（依關卡逐級往上；內容鎖定）</summary>
        InReview,
        /// <summary>已完成（簽核全數通過或無簽核流程；鎖定）</summary>
        Submitted,
        /// <summary>已撤銷（完成後撤回；資料保留不物理刪除）</summary>
        Cancelled,
    }

    public enum ApprovalStepStatus { Pending, Approved, Rejected }

    /// <summary>
    /// 簽核關卡的共同形狀。工作記錄與請假單各有自己的關卡表（欄位型別化、可各自加欄位），
    /// 但授權判斷（誰能簽這一關）走同一份實作 —— 見 ApprovalResolver.CanAct。
    /// </summary>
    public interface IApprovalStep
    {
        /// <summary>⚠ 四張快照表都有這個欄位；改派要靠它定位到具體某一關</summary>
        int Id { get; set; }
        int Round { get; set; }
        int StepOrder { get; set; }
        int? RoleId { get; set; }
        string? RoleName { get; set; }
        int? AssignedUserId { get; set; }
        string? AssignedUserName { get; set; }
        ApprovalStepStatus Status { get; set; }
        int? ActedBy { get; set; }
        string? ActedByName { get; set; }
        int? OnBehalfOfUserId { get; set; }
        string? OnBehalfOfName { get; set; }
        DateTime? ActedAt { get; set; }
        string? Comment { get; set; }
        /// <summary>會簽／任一位的名單（2026-10-09）；null ＝ 一般關卡</summary>
        string? CoSignJson { get; set; }
        string DisplayName { get; }
    }

    /// <summary>
    /// 一關「怎麼簽」（2026-10-09 業主定案，見 96-組織異動影響評估與社保組合併）。
    /// ⚠ 預設值維持各關卡原本的行為，既有流程不受影響。
    /// </summary>
    public enum ApprovalSignMode
    {
        /// <summary>
        /// 原本的行為：主管關卡＝送單的人指定一位（沒指定給第一位）；角色關卡＝有這個角色的任一位。
        /// </summary>
        Default,
        /// <summary>任一位：候選人裡任何一位簽了這一關就過（主管關卡用；角色關卡本來就是這樣）</summary>
        Any,
        /// <summary>會簽：候選人**全部**都要簽、**沒有順序**；最後一位簽完這一關才過，任一人退件＝整張退件</summary>
        All,
    }

    /// <summary>
    /// 關卡指派方式：
    /// Role＝該角色任一成員可簽（全院共通流程）；
    /// ManagerChain＝由送簽者組織單位往上第 N 層主管（人事單據用）；
    /// SpecificUser＝固定指定人；
    /// Counterpart＝這張單的對造當事人（目前只有調班用）。
    /// </summary>
    public enum ApprovalStepType
    {
        Role,
        ManagerChain,
        SpecificUser,

        /// <summary>
        /// ⭐⭐ **對造當事人** —— 調班的「被對調的那一位」。
        ///
        /// ⚠ 這一關嚴格說不是「簽核」，是**當事人合意**：
        ///   別人不能代他同意把自己的班換掉，連管理員都不該。
        ///   但它在流程上就是一關，硬要留在引擎外會變成
        ///   「一張單有兩套進度」——待辦清單、退件重送、稽核軌跡全都要各做一份。
        ///
        /// ⚠ 用既有三種表達不了：
        ///   角色 → 全院同角色的人都能替他同意
        ///   主管 → 對造不是申請人的主管
        ///   指定人 → 設定期就要寫死是誰，但對造是**每張單不同**
        /// </summary>
        Counterpart,
    }

    /// <summary>
    /// 表單簽核關卡設定（送簽時解析並快照到 WorkRecordApprovalStep）。
    ///
    /// ⚠ 2026-08-23 起關卡掛在 <see cref="ApprovalFlow"/> 底下，不再直接掛表單 ——
    ///   中間多一層才有地方放「什麼情況走哪一條」的條件。
    ///   表單只有一條流程時行為完全不變。
    /// </summary>
    public class FormApprovalStep
    {
        public int Id { get; set; }
        public int ApprovalFlowId { get; set; }
        public ApprovalFlow? ApprovalFlow { get; set; }
        public int StepOrder { get; set; }

        public ApprovalStepType StepType { get; set; } = ApprovalStepType.Role;

        /// <summary>StepType=Role 時使用</summary>
        public int? RoleId { get; set; }
        public Role? Role { get; set; }

        /// <summary>StepType=ManagerChain 時使用：往上第幾層主管（1=直屬）</summary>
        public int? ManagerLevel { get; set; }

        /// <summary>StepType=SpecificUser 時使用</summary>
        public int? SpecificUserId { get; set; }

        /// <summary>
        /// 這一關怎麼簽（2026-10-09）：預設／任一位／會簽。只對「主管」與「角色」關卡有意義。
        /// ⚠ 角色關卡選會簽＝**全院**有這個角色的在職人員都要簽。
        /// </summary>
        public ApprovalSignMode SignMode { get; set; } = ApprovalSignMode.Default;
    }

    /// <summary>
    /// 記錄簽核關卡（快照＋歷程合一）：每次送簽產生一輪（Round）關卡；
    /// 退件後重送 Round+1，舊輪保留為歷史，符合「可舉證性」要求。
    /// </summary>
    public class WorkRecordApprovalStep : IApprovalStep
    {
        public int Id { get; set; }
        public int WorkRecordId { get; set; }
        public WorkRecord? WorkRecord { get; set; }

        public int Round { get; set; }
        public int StepOrder { get; set; }

        /// <summary>依角色關卡：該角色成員可簽（SpecificUser/ManagerChain 解析後為 null）</summary>
        public int? RoleId { get; set; }
        [MaxLength(50)]
        public string? RoleName { get; set; }

        /// <summary>指名關卡：解析自主管鏈或指定人；有值時僅此人（或其代理人）可簽</summary>
        public int? AssignedUserId { get; set; }
        [MaxLength(50)]
        public string? AssignedUserName { get; set; }

        public ApprovalStepStatus Status { get; set; } = ApprovalStepStatus.Pending;
        public int? ActedBy { get; set; }
        [MaxLength(50)]
        public string? ActedByName { get; set; }

        /// <summary>代理簽核時：實際被代理的對象（代辦文化 — 操作者與當事人分開記錄）</summary>
        public int? OnBehalfOfUserId { get; set; }
        [MaxLength(50)]
        public string? OnBehalfOfName { get; set; }

        public DateTime? ActedAt { get; set; }
        [MaxLength(500)]
        public string? Comment { get; set; }

        /// <summary>關卡顯示名稱：指名關卡顯示人名，角色關卡顯示角色名</summary>
        /// <summary>會簽／任一位的名單（JSON，見 <see cref="CoSign"/>）；null ＝ 一般關卡</summary>
        public string? CoSignJson { get; set; }

        public string DisplayName => CoSign.Display(CoSignJson) ?? AssignedUserName ?? RoleName ?? "未指派";
    }

    /// <summary>
    /// 表單定義（設定化核心）：SchemaJson 描述欄位結構，前端動態渲染。
    /// 140+ 種表單共用此機制，新表單＝新一筆定義，不改程式。
    /// </summary>
    public class FormDefinition : IAuditable
    {
        /// <summary>
        /// ⚠⚠ 這裡列的表單定義**不是「可以填的表」**，只是借表單引擎承載簽核流程設定（SchemaJson 一律是 <c>{}</c>）。
        /// 例：新專案做了有自己資料表與頁面的請假單，要用同一套簽核流程設定 ⇒ 種一筆定義、代碼加進來。
        /// 它們不會出現在「新增表單」選單裡（否則會建出一筆內容空白、繞過業務規則的記錄）。
        /// </summary>
        public static readonly string[] ApprovalOnlyCodes = [];

        /// <summary>這筆定義是否只用來承載簽核設定（不可當工作記錄填寫）</summary>
        public bool IsApprovalOnly => ApprovalOnlyCodes.Contains(Code);

        public int Id { get; set; }
        public int InstitutionId { get; set; }

        /// <summary>表單代碼（如 life-journal＝保7 生活週誌）</summary>
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>分類（保育輔導員/社工/總務…，對應角色模組）</summary>
        [MaxLength(30)]
        public string? Category { get; set; }

        /// <summary>欄位結構 JSON（sections/fields/residentTable，見前端 DynamicRecordForm）</summary>
        public string SchemaJson { get; set; } = "{}";

        public FormFrequency Frequency { get; set; } = FormFrequency.Adhoc;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
    }

    /// <summary>工作記錄：內容 JSON＋多院童關聯＋標籤（一筆輸入、多處引用）</summary>
    public class WorkRecord : IAuditable, ISoftDelete
    {
        public int Id { get; set; }
        public int InstitutionId { get; set; }

        public int FormDefinitionId { get; set; }
        public FormDefinition? FormDefinition { get; set; }

        /// <summary>記錄歸屬日期（非填寫時間）</summary>
        public DateOnly RecordDate { get; set; }

        public RecordStatus Status { get; set; } = RecordStatus.Draft;
        public string ContentJson { get; set; } = "{}";
        public DateTime? SubmittedAt { get; set; }

        /// <summary>簽核中的目前關卡（對應 ApprovalSteps 最新一輪的 StepOrder）</summary>
        public int? CurrentStep { get; set; }

        public DateTime? CancelledAt { get; set; }
        public int? CancelledBy { get; set; }
        [MaxLength(200)]
        public string? CancelReason { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? DeletedAt { get; set; }

        public List<WorkRecordTag> Tags { get; set; } = [];
        public List<WorkRecordApprovalStep> ApprovalSteps { get; set; } = [];
    }

    /// <summary>記錄標籤（自由標籤；日後與評鑑指標對照掛接）</summary>
    public class WorkRecordTag
    {
        public int WorkRecordId { get; set; }
        public WorkRecord? WorkRecord { get; set; }

        [MaxLength(30)]
        public string Tag { get; set; } = string.Empty;
    }
}
