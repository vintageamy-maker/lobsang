using System.Text.RegularExpressions;

namespace Lobsang.Services.BookLookup
{
    /// <summary>
    /// 判斷使用者輸入的是哪一種號碼（ISBN／EAN／UPC／ISSN…），並驗證檢查碼。
    /// ⭐ 移植自 C:\Han\book\isbn.html 的 parseCode（2026-11-07），規則與訊息保持一致。
    /// </summary>
    public static class CodeParser
    {
        public record Parsed(string? Kind, string? Code, List<string> Notes, string? Error);

        public static Parsed Parse(string? raw)
        {
            var d = Regex.Replace((raw ?? "").ToUpperInvariant(), "[^0-9X]", "");
            var notes = new List<string>();
            if (d.Length == 0) return Err("請輸入 ISBN 或條碼號碼。");

            // EAN-13 ＋ 2 或 5 碼附加碼（掃描器有時會連在一起輸出）
            if ((d.Length == 15 || d.Length == 18) && IsDigits(d) && GtinValid(d[..13]))
            {
                notes.Add(AddonNote(d[13..]));
                d = d[..13];
            }
            if (d.Length == 10)
            {
                if (Regex.IsMatch(d, @"^\d{9}[\dX]$") && Check10(d)) return new("book", To13(d), notes, null);
                return Err("10 碼 ISBN 檢查碼不符，請確認有沒有打錯。");
            }
            if (!IsDigits(d)) return Err("號碼裡只能有數字（ISBN-10 最後一碼可以是 X）。");
            if (d.Length == 12)
            {
                if (!GtinValid(d)) return Err("12 碼 UPC 條碼檢查碼不符，請確認有沒有打錯。");
                notes.Add("12 碼 UPC 條碼已轉成 13 碼 EAN（前面補 0）");
                d = "0" + d;
            }
            if (d.Length == 8)
            {
                if (!GtinValid(d)) return Err("8 碼 EAN 條碼檢查碼不符，請確認有沒有打錯。");
                return new("product", d, notes, null);
            }
            if (d.Length != 13) return Err($"號碼長度不對：ISBN 是 10 或 13 碼，商品條碼是 8、12 或 13 碼（目前是 {d.Length} 碼）。");
            if (!GtinValid(d)) return Err("檢查碼不符，請確認有沒有打錯。");
            if (Regex.IsMatch(d, "^97[89]")) return new("book", d, notes, null);
            if (d.StartsWith("977")) return new("issn", d, notes, null);
            if (d.StartsWith("192")) return new("jp2", d, notes, null);
            if (Regex.IsMatch(d, "^(02|04|2)")) return new("store", d, notes, null);
            if (Regex.IsMatch(d, "^(05|98[1-4]|99)")) return new("coupon", d, notes, null);
            return new("product", d, notes, null);

            Parsed Err(string msg) => new(null, null, notes, msg);
        }

        static bool IsDigits(string s) => s.All(char.IsAsciiDigit);

        /// <summary>GTIN（EAN-8、UPC-A、EAN-13）通用檢查碼：從檢查碼往左，權重 3、1、3、1…</summary>
        public static bool GtinValid(string s)
        {
            if (s.Length == 0 || !IsDigits(s)) return false;
            int n = s.Length, sum = 0;
            for (int i = 0; i < n - 1; i++) sum += (s[i] - '0') * ((n - 1 - i) % 2 == 1 ? 3 : 1);
            return (10 - sum % 10) % 10 == s[n - 1] - '0';
        }

        static bool Check10(string s)
        {
            int sum = 0;
            for (int i = 0; i < 10; i++) sum += (s[i] == 'X' ? 10 : s[i] - '0') * (10 - i);
            return sum % 11 == 0;
        }

        static string Digit13(string s12)
        {
            int sum = 0;
            for (int i = 0; i < 12; i++) sum += (s12[i] - '0') * (i % 2 == 1 ? 3 : 1);
            return ((10 - sum % 10) % 10).ToString();
        }

        public static string To13(string s10) { var b = "978" + s10[..9]; return b + Digit13(b); }

        public static string? To10(string s13)
        {
            if (!s13.StartsWith("978") || s13.Length != 13) return null;
            var b = s13[3..12]; int sum = 0;
            for (int i = 0; i < 9; i++) sum += (b[i] - '0') * (10 - i);
            var c = (11 - sum % 11) % 11;
            return b + (c == 10 ? "X" : c.ToString());
        }

        /// <summary>雜誌 977 碼 → ISSN（含檢查碼）</summary>
        public static string IssnOf(string ean)
        {
            var s7 = ean[3..10]; int sum = 0;
            for (int i = 0; i < 7; i++) sum += (s7[i] - '0') * (8 - i);
            var c = (11 - sum % 11) % 11;
            return $"{s7[..4]}-{s7[4..]}{(c == 10 ? "X" : c.ToString())}";
        }

        static string AddonNote(string a)
        {
            var price = "";
            if (a.Length == 5)
            {
                var v = (int.Parse(a[1..]) / 100.0).ToString("0.00");
                if (a[0] == '5' && a != "59999") price = $"（建議售價 US${v}）";
                else if (a[0] == '6') price = $"（建議售價 CA${v}）";
                else if (a[0] == '0') price = $"（建議售價 £{v}）";
            }
            return $"已去掉條碼右側的附加碼「{a}」{price}";
        }

        /// <summary>書的出版地區：決定要查哪些資料庫</summary>
        public static string RegionOf(string i13) =>
            i13.StartsWith("9784") ? "jp" : Regex.IsMatch(i13, "^978(957|986|626)") ? "tw" : "west";

        public static readonly Dictionary<string, string> RegionLabel = new()
        {
            ["jp"] = "日本書", ["tw"] = "台灣書", ["west"] = "歐美／其他",
        };

        // GS1 前綴 → 條碼註冊地（代表廠商在哪裡申請條碼，不一定是產地）
        static readonly (int From, int To, string Name)[] Gs1 =
        [
            (0,19,"美國／加拿大"),(30,39,"美國"),(60,139,"美國／加拿大"),(300,379,"法國"),(380,380,"保加利亞"),(383,383,"斯洛維尼亞"),
            (385,385,"克羅埃西亞"),(387,387,"波士尼亞"),(389,389,"蒙特內哥羅"),(400,440,"德國"),(450,459,"日本"),(460,469,"俄羅斯"),
            (470,470,"吉爾吉斯"),(471,471,"台灣"),(474,474,"愛沙尼亞"),(475,475,"拉脫維亞"),(476,476,"亞塞拜然"),(477,477,"立陶宛"),
            (478,478,"烏茲別克"),(479,479,"斯里蘭卡"),(480,480,"菲律賓"),(481,481,"白俄羅斯"),(482,482,"烏克蘭"),(484,484,"摩爾多瓦"),
            (485,485,"亞美尼亞"),(486,486,"喬治亞"),(487,487,"哈薩克"),(489,489,"香港"),(490,499,"日本"),(500,509,"英國"),
            (520,521,"希臘"),(528,528,"黎巴嫩"),(529,529,"賽普勒斯"),(530,530,"阿爾巴尼亞"),(531,531,"北馬其頓"),(535,535,"馬爾他"),
            (539,539,"愛爾蘭"),(540,549,"比利時／盧森堡"),(560,560,"葡萄牙"),(569,569,"冰島"),(570,579,"丹麥"),(590,590,"波蘭"),
            (594,594,"羅馬尼亞"),(599,599,"匈牙利"),(600,601,"南非"),(611,611,"摩洛哥"),(619,619,"突尼西亞"),(622,622,"埃及"),
            (625,625,"約旦"),(626,626,"伊朗"),(628,628,"沙烏地阿拉伯"),(629,629,"阿拉伯聯合大公國"),(640,649,"芬蘭"),(690,699,"中國"),
            (700,709,"挪威"),(729,729,"以色列"),(730,739,"瑞典"),(750,750,"墨西哥"),(754,755,"加拿大"),(760,769,"瑞士"),
            (770,771,"哥倫比亞"),(775,775,"秘魯"),(778,779,"阿根廷"),(780,780,"智利"),(789,790,"巴西"),(800,839,"義大利"),
            (840,849,"西班牙"),(858,858,"斯洛伐克"),(859,859,"捷克"),(860,860,"塞爾維亞"),(865,865,"蒙古"),(868,869,"土耳其"),
            (870,879,"荷蘭"),(880,881,"南韓"),(883,883,"緬甸"),(884,884,"柬埔寨"),(885,885,"泰國"),(888,888,"新加坡"),
            (890,890,"印度"),(893,893,"越南"),(896,896,"巴基斯坦"),(899,899,"印尼"),(900,919,"奧地利"),(930,939,"澳洲"),
            (940,949,"紐西蘭"),(955,955,"馬來西亞"),(958,958,"澳門"),
        ];

        public static string? Gs1Country(string code)
        {
            if (code.Length < 3 || !int.TryParse(code[..3], out var p)) return null;
            foreach (var (from, to, name) in Gs1) if (p >= from && p <= to) return name;
            return null;
        }
    }
}
