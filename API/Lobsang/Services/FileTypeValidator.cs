using System.IO.Compression;
using Lobsang.Models;

namespace Lobsang.Services
{
    public record FileTypeResult(bool Ok, string ContentType, string Extension, string? Error);

    /// <summary>
    /// 上傳檔案的型別與大小把關。
    ///
    /// ⚠ **一律以檔頭位元組判定**，不信副檔名、也不信瀏覽器送來的 Content-Type ——
    /// 那兩個都由上傳端完全控制，拿來當驗證等於沒有驗。
    ///
    /// 採**白名單**：沒列到的一律拒絕。黑名單永遠列不完。
    /// </summary>
    public static class FileTypeValidator
    {
        /// <summary>全域硬上限。任何分類都不得超過（也要與 nginx 的 client_max_body_size 對齊）</summary>
        public const long MaxBytes = 25L * 1024 * 1024;

        private const long Mb = 1024 * 1024;

        /// <summary>各分類的大小上限（位元組）</summary>
        public static long LimitFor(FileCategory category) => category switch
        {
            // ⭐ 新分類需要不同上限時在這裡加（例：免登入的外部上傳給得最少，5 MB）
            // 掃描件多頁 PDF 會比較大
            _ => 20 * Mb,
        };

        /// <summary>各分類允許的內容型別。⭐ 來源不可信的分類（外部上傳）應只收圖片與 PDF</summary>
        public static IReadOnlySet<string> AllowedFor(FileCategory category) => category switch
        {
            _ => AllTypes,
        };

        private static readonly HashSet<string> AllTypes =
        [
            "image/jpeg", "image/png", "image/heic", "image/webp",
            "application/pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "application/msword", "application/vnd.ms-excel",
            "text/csv",
            "audio/mpeg", "audio/mp4", "audio/wav",
        ];

        private static readonly Dictionary<string, string> ExtOf = new()
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/heic"] = ".heic",
            ["image/webp"] = ".webp",
            ["application/pdf"] = ".pdf",
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
            ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = ".pptx",
            ["application/msword"] = ".doc",
            ["application/vnd.ms-excel"] = ".xls",
            ["text/csv"] = ".csv",
            ["audio/mpeg"] = ".mp3",
            ["audio/mp4"] = ".m4a",
            ["audio/wav"] = ".wav",
        };

        /// <summary>
        /// 依檔頭判定型別並檢查是否為該分類所允許。
        /// <paramref name="stream"/> 必須可 Seek；本方法會把位置歸零後再讀，結束時再歸零。
        /// </summary>
        public static FileTypeResult Inspect(Stream stream, FileCategory category, string originalName)
        {
            if (!stream.CanSeek)
                return new(false, "", "", "內部錯誤：檔案串流不可回捲");

            stream.Position = 0;
            var head = new byte[512];
            var n = stream.Read(head, 0, head.Length);
            stream.Position = 0;

            if (n == 0) return new(false, "", "", "檔案是空的");

            var type = Sniff(head, n, stream, originalName);
            if (type is null)
                return new(false, "", "",
                    "無法辨識的檔案格式。僅接受照片（JPG／PNG／HEIC／WEBP）、"
                    + "PDF、Word／Excel／PowerPoint、CSV 與語音檔");

            if (!AllowedFor(category).Contains(type))
                return new(false, type, "", $"此項目不接受這種檔案（{type}）");

            return new(true, type, ExtOf.GetValueOrDefault(type, ".bin"), null);
        }

        private static bool Starts(byte[] b, int n, params byte[] sig)
        {
            if (n < sig.Length) return false;
            for (var i = 0; i < sig.Length; i++) if (b[i] != sig[i]) return false;
            return true;
        }

        private static string? Sniff(byte[] b, int n, Stream stream, string originalName)
        {
            // ── 圖片 ──
            if (Starts(b, n, 0xFF, 0xD8, 0xFF)) return "image/jpeg";
            if (Starts(b, n, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return "image/png";
            // RIFF....WEBP
            if (n >= 12 && Starts(b, n, 0x52, 0x49, 0x46, 0x46)
                && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";
            // ISO-BMFF：....ftyp{brand}
            if (n >= 12 && b[4] == 'f' && b[5] == 't' && b[6] == 'y' && b[7] == 'p')
            {
                var brand = System.Text.Encoding.ASCII.GetString(b, 8, 4);
                if (brand is "heic" or "heix" or "hevc" or "mif1" or "msf1") return "image/heic";
                if (brand.StartsWith("M4A") || brand is "mp42" or "isom") return "audio/mp4";
            }

            // ── PDF ──
            if (Starts(b, n, 0x25, 0x50, 0x44, 0x46)) return "application/pdf";

            // ── 語音 ──
            if (Starts(b, n, 0x49, 0x44, 0x33)) return "audio/mpeg";              // ID3
            if (n >= 2 && b[0] == 0xFF && (b[1] & 0xE0) == 0xE0) return "audio/mpeg";
            if (n >= 12 && Starts(b, n, 0x52, 0x49, 0x46, 0x46)
                && b[8] == 'W' && b[9] == 'A' && b[10] == 'V' && b[11] == 'E') return "audio/wav";

            // ── 舊版 Office（OLE 複合文件）──
            // ⚠ doc 與 xls 的檔頭完全相同，光看位元組分不出來；
            //    這是唯一必須參考副檔名的地方，且僅在檔頭已確認是 OLE 之後。
            if (Starts(b, n, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1))
            {
                var ext = Path.GetExtension(originalName).ToLowerInvariant();
                return ext switch
                {
                    ".xls" => "application/vnd.ms-excel",
                    ".doc" => "application/msword",
                    _ => null,
                };
            }

            // ── OOXML ──
            // ⚠ docx／xlsx／pptx **本身就是 ZIP**，所以不能對 PK 檔頭一律放行，
            //    否則任何 .zip 改個副檔名就進得來。必須打開來看內容。
            if (Starts(b, n, 0x50, 0x4B, 0x03, 0x04)) return SniffOoxml(stream);

            return null;
        }

        /// <summary>
        /// 打開 ZIP 看它是不是真的 OOXML。
        /// 判準是 <c>[Content_Types].xml</c> 加上各格式專屬的目錄，
        /// 這樣純 ZIP（內容無從檢查，一律拒絕）就進不來。
        /// </summary>
        private static string? SniffOoxml(Stream stream)
        {
            try
            {
                stream.Position = 0;
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                var names = zip.Entries.Select(e => e.FullName).ToList();
                if (!names.Contains("[Content_Types].xml")) return null;

                if (names.Any(x => x.StartsWith("word/")))
                    return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                if (names.Any(x => x.StartsWith("xl/")))
                    return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                if (names.Any(x => x.StartsWith("ppt/")))
                    return "application/vnd.openxmlformats-officedocument.presentationml.presentation";
                return null;
            }
            catch
            {
                return null;   // 壞掉的 ZIP 一律拒絕
            }
            finally
            {
                stream.Position = 0;
            }
        }

        /// <summary>
        /// CSV 沒有檔頭可驗（純文字），故不走 <see cref="Sniff"/>，
        /// 由呼叫端在確定分類允許時另行處理。這裡只提供一致的判定。
        /// </summary>
        public static bool LooksLikeText(byte[] head, int n)
        {
            for (var i = 0; i < n; i++)
                if (head[i] == 0) return false;   // 有 NUL 就不是文字
            return true;
        }
    }
}
