using System.Text.Json;

namespace Lobsang.Models
{
    /// <summary>
    /// 一關的「會簽／任一位」名單（2026-10-09，見 96-組織異動影響評估與社保組合併）。
    ///
    /// ── 為什麼掛在關卡上，而不是一人一關 ──────────────────────────
    /// 7 種簽核單都是「每一關只有一筆紀錄」（唯一索引 單據＋Round＋StepOrder），
    /// 也只記「現在輪到第幾關」。業主要的會簽是**沒有順序、全部都要簽**：
    ///   ⇒ 這一關仍是一關，底下多掛一張名單（存在關卡快照的 CoSignJson）；
    ///     名單上的人同時看到、各簽各的；最後一位簽完，這一關才算過。
    ///   ⇒ 不動唯一索引，也不動各單據「往下一關」的邏輯。
    ///
    /// ── 規則 ────────────────────────────────────────────────────
    ///   任一位（any）：名單上任何一位簽了就過。
    ///   會簽（all）　：名單上每一位都要同意；任一人退件＝整張退件。
    ///   名單在**送出當下**固定（同關卡快照），之後有人取得或失去角色都不影響。
    ///   代理人可以代他的被代理人簽；管理員救急代簽＝直接完成這一關（照舊標示為代簽）。
    /// </summary>
    public class CoSign
    {
        public const string ModeAny = "any";
        public const string ModeAll = "all";
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Rejected = "rejected";

        public string Mode { get; set; } = ModeAll;
        public List<Signer> Signers { get; set; } = [];

        public class Signer
        {
            public int UserId { get; set; }
            public string Name { get; set; } = "";
            public string Status { get; set; } = Pending;
            public int? ActedBy { get; set; }
            public string? ActedByName { get; set; }
            public DateTime? ActedAt { get; set; }
            public string? Comment { get; set; }
        }

        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public static CoSign? Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<CoSign>(json, Json); }
            catch (JsonException) { return null; }
        }

        public string ToJson() => JsonSerializer.Serialize(this, Json);

        /// <summary>還能簽的人：任一位＝全部名單；會簽＝還沒簽的人</summary>
        public IEnumerable<Signer> Open =>
            Mode == ModeAny ? Signers : Signers.Where(s => s.Status == Pending);

        /// <summary>
        /// 關卡顯示名稱，例：「會簽：張三✓、李四」「任一位：張三／李四」。
        /// ⚠ 各單據的待簽清單、時間軸都用 DisplayName ⇒ 會簽進度不必改 7 個畫面就看得到。
        /// </summary>
        public static string? Display(string? json)
        {
            var cs = Parse(json);
            if (cs is null || cs.Signers.Count == 0) return null;
            if (cs.Mode == ModeAny)
                return "任一位：" + string.Join("／", cs.Signers.Select(s => s.Status == Approved ? s.Name + "✓" : s.Name));
            return "會簽：" + string.Join("、", cs.Signers.Select(s => s.Status switch
            {
                Approved => s.Name + "✓",
                Rejected => s.Name + "✗",
                _ => s.Name,
            }));
        }
    }
}
