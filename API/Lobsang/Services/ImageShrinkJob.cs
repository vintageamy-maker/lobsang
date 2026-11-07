using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Lobsang.Data;
using Lobsang.Models;

namespace Lobsang.Services
{
    /// <summary>
    /// 一次性：把「縮圖功能上線前」已存的照片縮小（2026-09-28 業主：舊的也縮小）。
    ///
    /// 用法（在 API 容器裡）：
    ///   <c>dotnet SmartCare.dll --shrink-images</c>          只列清單，什麼都不改
    ///   <c>dotnet SmartCare.dll --shrink-images --apply</c>  真的做
    ///
    /// ⚠ 做法是「另存一份新檔、改資料庫指向新檔」，**舊檔不刪**：
    ///   清單印出舊路徑，確認畫面都正常後再另外刪（刪東西要先列給業主看）。
    ///   萬一要退回，把資料庫那幾欄改回稽核紀錄裡的前值即可。
    /// ⚠ 可以重跑：已縮過的檔（邊長在上限內且夠小）TryShrink 會回 null，自動跳過。
    /// </summary>
    public static class ImageShrinkJob
    {
        public static async Task RunAsync(IServiceProvider sp, bool apply, TextWriter log, CancellationToken ct = default)
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var local = sp.GetRequiredService<LocalFileStorage>();
            var gcs = sp.GetService<GcsFileStorage>();
            var json = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

            var files = await db.StoredFiles.IgnoreQueryFilters()
                .Where(f => f.DeletedAt == null && f.ContentType.StartsWith("image/"))
                .OrderBy(f => f.Id)
                .ToListAsync(ct);

            log.WriteLine($"模式：{(apply ? "執行（--apply）" : "只列清單")}；圖片共 {files.Count} 張");
            long before = 0, after = 0;
            int done = 0, skipped = 0, failed = 0;

            foreach (var f in files)
            {
                IFileStorage? store = f.Backend == StorageBackend.Gcs ? gcs : local;
                if (store is null) { log.WriteLine($"  #{f.Id} 略過：GCS 未設定"); failed++; continue; }

                byte[] bytes;
                try
                {
                    await using var s = await store.OpenAsync(f.ObjectKey, ct);
                    using var ms = new MemoryStream();
                    await s.CopyToAsync(ms, ct);
                    bytes = ms.ToArray();
                }
                catch (Exception ex)
                {
                    log.WriteLine($"  #{f.Id} 讀不到原檔：{ex.GetType().Name}");
                    failed++;
                    continue;
                }

                var r = ImageShrinker.TryShrink(bytes, f.ContentType);
                if (r is null) { skipped++; continue; }

                before += bytes.LongLength;
                after += r.Bytes.LongLength;
                log.WriteLine($"  #{f.Id} {f.Category}/{f.OwnerType} {bytes.LongLength / 1024} KB → {r.Bytes.LongLength / 1024} KB（{r.Width}x{r.Height}）舊檔 {f.Backend}:{f.ObjectKey}");
                if (!apply) { done++; continue; }

                var key = ObjectKeys.Build(f.InstitutionId, f.Category, ".jpg", DateTime.UtcNow.AddHours(8));
                await store.SaveAsync(key, new MemoryStream(r.Bytes), "image/jpeg", ct);

                var old = new { f.ObjectKey, f.ContentType, f.SizeBytes, f.Sha256, f.OriginalName };
                f.ObjectKey = key;
                f.ContentType = "image/jpeg";
                f.SizeBytes = r.Bytes.LongLength;
                f.Sha256 = Convert.ToHexString(SHA256.HashData(r.Bytes)).ToLowerInvariant();
                f.OriginalName = ImageShrinker.JpgName(f.OriginalName);

                db.AuditLogs.Add(new AuditLog
                {
                    InstitutionId = f.InstitutionId,
                    UserId = null,
                    Action = "file.shrink",
                    TargetType = "stored_file",
                    TargetId = f.Id,
                    BeforeJson = JsonSerializer.Serialize(old, json),
                    AfterJson = JsonSerializer.Serialize(new { f.ObjectKey, f.ContentType, f.SizeBytes, f.Sha256, f.OriginalName }, json),
                    CreatedAt = DateTime.UtcNow,
                });
                // 一張一存：中途失敗時，已完成的不會因為後面那張而一起退回
                await db.SaveChangesAsync(ct);
                done++;
            }

            log.WriteLine($"要縮／已縮 {done} 張、不需要 {skipped} 張、失敗 {failed} 張；"
                        + $"{before / 1024} KB → {after / 1024} KB");
        }
    }
}
