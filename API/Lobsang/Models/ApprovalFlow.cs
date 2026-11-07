using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Lobsang.Models
{
    /// <summary>
    /// 條件要拿什麼來比。
    ///
    /// ⚠ <see cref="None"/> 代表**無條件**：整張表單只有一條流程，永遠適用。
    ///   這是目前所有表單的狀態，也是升級後的預設值 —— 行為與加這一層之前完全一樣。
    /// </summary>
    public enum ApprovalConditionKind
    {
        /// <summary>無條件（一張表單只有一條流程）</summary>
        None,
        /// <summary>天數 —— 請假「三天以內組長就能准」那種</summary>
        Days,
        /// <summary>時數 —— 加班</summary>
        Hours,
        /// <summary>金額 —— 採購／請款（育幼院目前沒有，但遲早會有）</summary>
        Amount,
        /// <summary>
        /// 事件等級 —— 危機事件紀錄表（2026-09-22）。一般＝1、重大＝2、重大危機＝3，
        /// 督導改級時以改級後的等級為準（見 <see cref="Lobsang.Services.CrisisLevel"/>）。
        /// 業主裁示預設：第一級到社工督導、第二級以上到院長；不足之處院方在簽核流程設定自行調整。
        /// </summary>
        Level,
    }

    /// <summary>
    /// 簽核**流程**：表單與關卡之間的那一層。
    ///
    /// <code>
    /// 加這一層之前：  表單 ───────────────► 關卡
    /// 加這一層之後：  表單 ──► 流程(條件) ──► 關卡
    ///                          ▲
    ///                    「三天以內」放這裡
    /// </code>
    ///
    /// ── 為什麼需要 ─────────────────────────────────────────────────
    /// 原本關卡直接掛在表單上，**沒有任何地方能表達「什麼情況走哪一條」**。
    /// 想做「請三天以內組長准、超過要院長」就只能改程式。
    ///
    /// ── ⭐⭐ 級距用半開區間 [Min, Max) ────────────────────────────
    /// 上限**不含**。這是抄會計那套踩過的坑：若寫成閉區間再要求
    /// 「前一段上限 + 1 == 下一段下限」，遇到小數就會有縫 ——
    /// 例如 0~3 與 4~10 之間，**3.5 天兩邊都掉不進去**，
    /// 而且錯誤訊息會是「查不到適用流程」這種看不懂的話。
    /// ⭐ 半開區間對任何小數都不會有縫，而且「未滿三天」正好就是口語的意思。
    ///
    /// ── ⚠ 這一層不影響在途單據 ──────────────────────────────────
    /// 送簽當下就把關卡解析成快照寫在單上，之後改流程不會動到已送出的單。
    /// </summary>
    public class ApprovalFlow : IAuditable
    {
        public int Id { get; set; }
        public int InstitutionId { get; set; }

        public int FormDefinitionId { get; set; }
        public FormDefinition? FormDefinition { get; set; }

        /// <summary>
        /// ⚠ 同一張表單底下的所有流程**必須是同一種 Kind** ——
        /// 「三天以內」和「一萬以下」混在同一組級距裡沒有意義，也比不出來。
        /// </summary>
        public ApprovalConditionKind ConditionKind { get; set; } = ApprovalConditionKind.None;

        /// <summary>下限，**含**。一組級距的第一段必須是 0</summary>
        [Precision(18, 4)]
        public decimal MinValue { get; set; }

        /// <summary>
        /// 上限，**不含**。<c>null</c> ＝ 無上限；
        /// ⚠ 一組級距裡**有且只有最後一段**是 null，否則會有值落不進任何一段。
        /// </summary>
        [Precision(18, 4)]
        public decimal? MaxValue { get; set; }

        /// <summary>
        /// 顯示名稱（例：「三天以內」）。
        /// ⚠⚠ **純顯示，不參與任何判斷。** 判斷一律看 Min/Max ——
        /// 把顯示字串拿去當條件，改個措辭就讓流程失效。
        /// </summary>
        [MaxLength(50)]
        public string? Name { get; set; }

        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }

        public List<FormApprovalStep> Steps { get; set; } = [];

        /// <summary>這個值落在這一段裡嗎（半開區間）</summary>
        public bool Covers(decimal value) =>
            value >= MinValue && (MaxValue is null || value < MaxValue.Value);
    }

    /// <summary>
    /// ⚠⚠ **哪些表單才有數字可以分流。**
    ///
    /// 條件要能用，前提是送簽時**系統真的拿得出那個數字**：
    ///   請假 → 天數／時數 ✅
    ///   加班 → 時數 ✅
    ///   補卡／生活週誌／調班 → **沒有任何數字** ❌
    ///
    /// 在沒有數字的表單上設級距，PickFlowAsync 比不出來只能退回預設那條 ——
    /// 畫面上看起來設好了，實際上永遠只走同一條。
    /// ⭐ 這正是本專案一再出現的那種錯：**做了一個不會生效的設定畫面。**
    /// ⇒ 乾脆不讓設。日後某張單開始提供數字，把它加進這個清單即可。
    /// </summary>
    public static class ApprovalConditionSupport
    {
        // ⭐ 請款單 2026-08-23 加入：大額／小額的差別就是金額，直接用既有的級距，
        //    不另做一套「大額流程」。
        /// <summary>
        /// ⚠⚠ **這是唯一一份清單。** 查詢端要判斷「這張表單能不能分流」時直接用它，
        ///   不可以在別處再抄一份 —— 2026-08-23 就發生過：控制器裡另外寫死
        ///   <c>f.Code == "leave-request" || f.Code == "overtime"</c>，
        ///   加了請款單之後這裡改了、那裡沒改，畫面上請款單就是不給分流。
        ///   ⭐ 判準（又一次）：**同一條規則寫在兩個地方，一定會有一天對不起來。**
        /// </summary>
        public static readonly string[] SupportedCodes = ["leave-request", "overtime", "payment-request", "crisis-event"];

        /// <summary>
        /// 每張表單**拿得出哪幾種數字**。⚠ 只能用這裡列的——危機事件沒有天數、請假沒有事件等級，
        /// 選錯了一樣是「設了永遠比不出來」。
        /// </summary>
        public static ApprovalConditionKind[] KindsFor(string? formCode) => formCode switch
        {
            "leave-request" => [ApprovalConditionKind.Days, ApprovalConditionKind.Hours],
            "overtime" => [ApprovalConditionKind.Hours],
            "payment-request" => [ApprovalConditionKind.Amount],
            "crisis-event" => [ApprovalConditionKind.Level],
            _ => [],
        };

        public static bool Supports(string? formCode) => formCode is not null && SupportedCodes.Contains(formCode);

        public static bool Supports(string? formCode, ApprovalConditionKind kind) => KindsFor(formCode).Contains(kind);

        public static string Reason(string? formName) =>
            $"「{formName}」送簽時沒有可以比的數字（天數／時數／金額），"
            + "設了級距也永遠比不出來，只會一直走同一條。目前只有請假單、加班單、請款單與危機事件紀錄表可以分流。";
    }

    /// <summary>
    /// 送簽時可拿來比對條件的事實。
    /// ⚠ 一張單可能同時知道天數與時數（請假），由流程的 <see cref="ApprovalConditionKind"/>
    ///   決定要拿哪一個來比 —— 呼叫端不必知道院方設了什麼條件。
    /// </summary>
    public record ApprovalFacts(decimal? Days = null, decimal? Hours = null, decimal? Amount = null, decimal? Level = null)
    {
        public static readonly ApprovalFacts Empty = new();

        public decimal? ValueFor(ApprovalConditionKind kind) => kind switch
        {
            ApprovalConditionKind.Days => Days,
            ApprovalConditionKind.Hours => Hours,
            ApprovalConditionKind.Amount => Amount,
            ApprovalConditionKind.Level => Level,
            _ => null,
        };
    }

    /// <summary>
    /// 級距完整性檢核。
    ///
    /// ⚠⚠ 級距有縫或重疊時，症狀是「某些單子送不出去」或「兩條流程都命中」，
    ///   而且都要等到有人真的送那個金額／天數才會爆。**在存檔當下就要擋。**
    /// </summary>
    public static class ApprovalTierValidator
    {
        public record Tier(decimal Min, decimal? Max);

        /// <summary>回傳錯誤說明；null ＝ 沒問題</summary>
        public static string? Validate(IReadOnlyList<Tier> tiers)
        {
            if (tiers.Count == 0) return "至少要有一段。";

            var open = tiers.Count(t => t.Max is null);
            if (open == 0) return "最後一段必須是「以上不限」（不填上限）。";
            if (open > 1) return "只能有一段是「以上不限」。";

            foreach (var t in tiers)
                if (t.Max is decimal m && m <= t.Min)
                    return $"「{t.Min} 起」這一段的上限（{m}）必須大於下限。";

            // 無上限那段一定排在最後
            var sorted = tiers.OrderBy(t => t.Max is null ? 1 : 0).ThenBy(t => t.Min).ToList();

            if (sorted[0].Min != 0) return "第一段必須從 0 開始。";

            for (var i = 1; i < sorted.Count; i++)
            {
                var prev = sorted[i - 1];
                var cur = sorted[i];
                if (prev.Max is null) return "「以上不限」的那一段後面不能再有級距。";
                if (prev.Max.Value < cur.Min)
                    return $"{prev.Max.Value} 到 {cur.Min} 之間沒有任何級距涵蓋。";
                if (prev.Max.Value > cur.Min)
                    return $"{cur.Min} 到 {prev.Max.Value} 之間有兩段重疊。";
            }
            return null;
        }
    }
}
