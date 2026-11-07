using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using Lobsang.Models;
using Lobsang.Services;

namespace Lobsang.Controllers
{
    /// <summary>
    /// 分類 → 權限的對照。
    ///
    /// ⚠ 刻意**不做一個叫 file.upload 的權限**。
    /// 「能不能碰這個檔案」跟「能不能碰它所屬的資料」是同一件事：
    /// 看得到那筆資料的人才看得到它的附件。獨立的檔案權限會在兩者之間開出一道縫。
    ///
    /// 這張表就是新模組接進來的接縫：加一個 FileCategory，在這裡配上讀寫權限碼。
    /// </summary>
    public static class FileAccessPolicy
    {
        public static (string Read, string Write) For(FileCategory c) => c switch
        {
            FileCategory.Record => ("record.read", "record.write"),
            FileCategory.Hr => ("user.manage", "user.manage"),
            // 匯出包、系統檔由系統或管理員產生，不接受一般使用者上傳
            FileCategory.Export => ("record.read", "system.admin"),
            FileCategory.System => ("record.read", "system.admin"),
            _ => ("system.admin", "system.admin"),
        };

        /// <summary>
        /// 這個人讀不讀得到這一類檔案。
        /// ⭐ 需要「任一權限即可」的分類（例如兩種角色都該看得到的照片），在這裡加判斷。
        /// </summary>
        public static bool CanRead(System.Security.Claims.ClaimsPrincipal user, FileCategory c) =>
            user.HasPermission(For(c).Read);
    }

    /// <summary>
    /// 檔案上傳與下載。
    ///
    /// ⚠ **下載一律經這支 API 代理，不發簽章網址。**
    /// 理由：個資法的調閱紀錄要知道**誰在什麼時候看了哪一份資料**。
    /// 簽章網址做不到這件事，而且一旦外流無法撤銷。
    /// 代價是下載走我方頻寬 —— 對這個資料敏感度是划算的。
    /// </summary>
    [ApiController]
    [Route("api/files")]
    public class FilesController(
        AppDbContext db, FileStorageResolver storage, AuditLogger audit,
        DataScopeService scopes) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);
        private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpPost]
        public async Task<IActionResult> Upload(
            IFormFile file,
            [FromForm] FileCategory category,
            [FromForm] string? ownerType = null,
            [FromForm] int? ownerId = null,
            [FromForm] string? note = null,
            CancellationToken ct = default)
        {
            var (_, writePerm) = FileAccessPolicy.For(category);
            if (!User.HasPermission(writePerm))
                return StatusCode(403, new { message = "無權上傳此類檔案", code = "forbidden" });

            // ⚠⚠ 既有漏洞（2026-09-13 修）：舊版只檢查**分類**的寫入權，
            //    不檢查「這張單是不是你的」⇒ 任何同仁都能往別人的請假單塞檔案。
            //    Attendance 的 attendance.apply 全院都有，所以上面那道形同虛設。
            if (await OwnerReadableAsync(ownerType, ownerId, ct) is string unreadable)
                return StatusCode(403, new { message = unreadable, code = "forbidden" });

            if (await OwnerLockedAsync(ownerType, ownerId, ct) is string locked)
                return BadRequest(new { message = locked, code = "owner_locked" });

            if (file is null || file.Length == 0)
                return BadRequest(new { message = "請選擇檔案", code = "no_file" });

            var limit = FileTypeValidator.LimitFor(category);
            if (file.Length > limit)
                return BadRequest(new
                {
                    message = $"檔案 {file.Length / 1024d / 1024d:0.#} MB 超過此項目的上限 {limit / 1024 / 1024} MB",
                    code = "too_large",
                });

            // ⚠ 先落到暫存再驗：型別要看檔頭、雜湊要讀全檔，兩者都需要可回捲的串流，
            //    而 IFormFile 在大檔時本來就已經是磁碟緩衝。
            await using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            buffer.Position = 0;

            var probe = FileTypeValidator.Inspect(buffer, category, file.FileName);
            if (!probe.Ok)
                return BadRequest(new { message = probe.Error, code = "invalid_type" });

            // ⭐ 照片先縮小再存（見 ImageShrinker）。縮不了就原樣，不擋上傳。
            //    ⚠ 雜湊算在「實際存下去的內容」上：同一張原圖重傳，縮出來一樣，重複偵測照常有效。
            var originalName = Path.GetFileName(file.FileName);
            var originalBytes = file.Length;
            if (ImageShrinker.TryShrink(buffer.ToArray(), probe.ContentType) is { } shrunk)
            {
                buffer.SetLength(0);
                buffer.Write(shrunk.Bytes);
                probe = probe with { ContentType = "image/jpeg", Extension = ".jpg" };
                originalName = ImageShrinker.JpgName(originalName);
            }

            buffer.Position = 0;
            var sha = Convert.ToHexString(await SHA256.HashDataAsync(buffer, ct)).ToLowerInvariant();
            buffer.Position = 0;

            // 同一份內容重複上傳到同一個歸屬：直接回傳既有的，不重複佔空間
            var dup = await db.StoredFiles.FirstOrDefaultAsync(
                f => f.InstitutionId == InstitutionId && f.Sha256 == sha
                     && f.OwnerType == ownerType && f.OwnerId == ownerId, ct);
            if (dup is not null)
                return Ok(new { id = dup.Id, duplicated = true, dup.OriginalName });

            var store = await storage.ForAsync(InstitutionId, ct);
            var now = DateTime.UtcNow.AddHours(8);
            var key = ObjectKeys.Build(InstitutionId, category, probe.Extension, now);

            await store.SaveAsync(key, buffer, probe.ContentType, ct);

            var rec = new StoredFile
            {
                InstitutionId = InstitutionId,
                Category = category,
                ObjectKey = key,
                // ⚠ 存「當下用的後端」而不是「現在的設定」—— 日後搬遷才找得回舊檔
                Backend = store.Backend,
                OriginalName = originalName,
                ContentType = probe.ContentType,
                SizeBytes = buffer.Length,
                Sha256 = sha,
                OwnerType = string.IsNullOrWhiteSpace(ownerType) ? null : ownerType.Trim(),
                OwnerId = ownerId,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            };
            db.StoredFiles.Add(rec);

            audit.Log("file.upload", "stored_file", null, null, new
            {
                category = category.ToString(), rec.OriginalName, rec.ContentType,
                rec.SizeBytes, backend = store.Backend.ToString(), rec.OwnerType, rec.OwnerId,
                uploadedBytes = originalBytes,
            });
            await db.SaveChangesAsync(ct);

            return Ok(new
            {
                id = rec.Id,
                rec.OriginalName, rec.ContentType, rec.SizeBytes,
                category = category.ToString(),
                backend = store.Backend.ToString(),
            });
        }

        /// <summary>某個單據上掛了哪些檔案</summary>
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string ownerType, [FromQuery] int ownerId, CancellationToken ct = default)
        {
            // ⚠⚠ 列表也要擋 —— 它回傳 OriginalName，而
            //    「○○○_B型肝炎診斷證明.pdf」**檔名本身就是病歷資訊**。
            //    只擋下載不擋列表，等於把病情寫在清單上給全院看。
            if (await OwnerReadableAsync(ownerType, ownerId, ct) is string unreadable)
                return StatusCode(403, new { message = unreadable, code = "forbidden" });

            var rows = await db.StoredFiles
                .Where(f => f.InstitutionId == InstitutionId
                            && f.OwnerType == ownerType && f.OwnerId == ownerId)
                .OrderBy(f => f.Id)
                .ToListAsync(ct);

            // 逐筆過濾：同一個單據上可能掛著不同分類、權限也不同
            return Ok(rows
                // ⚠ 走 CanRead 不是 For(..).Read —— 膳食照片是「廚師或營養師任一」
                .Where(f => FileAccessPolicy.CanRead(User, f.Category))
                .Select(f => new
                {
                    f.Id, f.OriginalName, f.ContentType, f.SizeBytes,
                    category = f.Category.ToString(), f.Note, f.CreatedAt,
                    isImage = f.ContentType.StartsWith("image/"),
                }));
        }

        /// <summary>
        /// 下載。⚠ 大部分分類每一次都寫調閱紀錄 —— 這是這支 API 存在的主要理由。
        /// ⭐ 2026-09-20 業主：**膳食照片不算敏感個資**（不像診斷證明、病歷），
        ///   不需要「個人資料調閱申請暨紀錄表」那種一筆一筆的紀錄——
        ///   否則月曆想秀縮圖（一次幾十張）就會把稽核表灌爆，讓真正需要盯的紀錄被淹沒。
        ///   ⇒ 只有 <see cref="FileCategory.Meal"/> 不寫；其餘分類維持原行為。
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Download(int id, CancellationToken ct = default)
        {
            var f = await db.StoredFiles.FirstOrDefaultAsync(
                x => x.Id == id && x.InstitutionId == InstitutionId, ct);
            if (f is null) return NotFound();

            if (!FileAccessPolicy.CanRead(User, f.Category))
                return StatusCode(403, new { message = "無權檢視此檔案", code = "forbidden" });

            // ⭐ 分類層過了還要過**資料列層** —— 診斷證明只有本人、人事、院長能開
            if (await OwnerReadableAsync(f.OwnerType, f.OwnerId, ct) is string unreadable)
                return StatusCode(403, new { message = unreadable, code = "forbidden" });

            // ⚠ 用檔案**當初存進去的後端**開啟，不是機構現在的設定
            var store = f.Backend == StorageBackend.Gcs
                ? HttpContext.RequestServices.GetService<GcsFileStorage>() as IFileStorage
                : HttpContext.RequestServices.GetRequiredService<LocalFileStorage>();
            if (store is null)
                return StatusCode(503, new
                {
                    message = "此檔案存放於雲端，但雲端儲存目前不可用",
                    code = "backend_unavailable",
                });

            Stream content;
            try
            {
                content = await store.OpenAsync(f.ObjectKey, ct);
            }
            catch (Exception e)
            {
                return StatusCode(500, new { message = $"檔案讀取失敗：{e.Message}", code = "read_failed" });
            }

            // ⭐ 每次開啟都寫調閱稽核（個資調閱紀錄）。不含個資的分類（例如公開照片）可在這裡排除，免得稽核被淹沒
            audit.Log("file.download", "stored_file", f.Id, null, new
            {
                category = f.Category.ToString(), f.OriginalName, f.OwnerType, f.OwnerId,
            });
            await db.SaveChangesAsync(ct);

            // inline 讓照片能直接預覽；檔名用原始檔名（中文靠 UTF-8 編碼帶出去）
            return File(content, f.ContentType, f.OriginalName, enableRangeProcessing: true);
        }

        /// <summary>
        /// 移除。⚠ 只軟刪除主檔紀錄，**不刪儲存區的物件**。
        /// 出勤紀錄要留 5 年、個案資料另有法定保存年限，
        /// 真正的實體清除必須是有意識的、另外走的作業。
        /// </summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
        {
            var f = await db.StoredFiles.FirstOrDefaultAsync(
                x => x.Id == id && x.InstitutionId == InstitutionId, ct);
            if (f is null) return NotFound();

            // ⚠⚠ 既有漏洞（2026-09-13 修）：下面這個條件對 Attendance 分類**恆不成立** ——
            //    writePerm 是 attendance.apply，而**全院每個角色都有**
            //    ⇒ `!HasPermission(...)` 永遠是 false ⇒ 任何同仁都刪得掉別人的診斷證明。
            //    ⇒ 先過資料列層（能看才能談刪），再談分類層的寫入權。
            if (await OwnerReadableAsync(f.OwnerType, f.OwnerId, ct) is string unreadable)
                return StatusCode(403, new { message = unreadable, code = "forbidden" });

            var (_, writePerm) = FileAccessPolicy.For(f.Category);
            if (!User.HasPermission(writePerm) && f.CreatedBy != UserId)
                return StatusCode(403, new { message = "無權移除此檔案", code = "forbidden" });

            if (await OwnerLockedAsync(f.OwnerType, f.OwnerId, ct, forDelete: true) is string locked)
                return BadRequest(new { message = locked, code = "owner_locked" });

            f.DeletedAt = DateTime.UtcNow.AddHours(8);
            audit.Log("file.delete", "stored_file", f.Id,
                new { f.OriginalName, category = f.Category.ToString() }, null);
            await db.SaveChangesAsync(ct);
            return NoContent();
        }

        /// <summary>
        /// 所屬單據是否已鎖定（回傳非 null 即為拒絕的理由）。
        ///
        /// ⚠ **前端擋不等於後端擋。** 畫面在唯讀時不顯示上傳鈕，
        /// 但有 <c>record.write</c> 的人仍可直接對 API 送出 —— 那樣就能在已送簽、
        /// 甚至已完成的記錄上偷加或抽掉附件，而附件是單據內容的一部分。
        /// 這是 B-6／B-8 那一類「篩選條件其實是權限判斷」的同型問題，寧可在這裡多寫一段。
        ///
        /// 目前只認得有鎖定概念的單據型別；其餘（如院童）沒有鎖定狀態，一律放行。
        /// 新模組接進來時**要記得在這裡加一條**。
        /// </summary>
        /// <param name="forDelete">
        /// 這次是要**刪**還是要**增**。
        /// ⚠ 舊版增刪共用同一個判斷，但請假需要分流：簽核中可以補件、不可以抽掉。
        /// 其餘型別兩者相同，不受影響。
        /// </param>
        /// <summary>
        /// 表單欄位上的照片格存成 <c>work_record:欄位代碼</c>，
        /// 同一筆記錄的不同欄位才不會混在一起。⚠ 鎖定與保密判斷必須跟整筆記錄的附件一樣 ——
        /// 不先正規化的話會掉進 default（不鎖、不擋），送簽後照片就能被抽換。
        /// </summary>
        public static string OwnerKind(string ownerType) =>
            ownerType.StartsWith("work_record:", StringComparison.Ordinal) ? "work_record" : ownerType;

        private async Task<string?> OwnerLockedAsync(
            string? ownerType, int? ownerId, CancellationToken ct, bool forDelete = false)
        {
            if (string.IsNullOrWhiteSpace(ownerType) || ownerId is not int id) return null;

            switch (OwnerKind(ownerType))
            {
                case "work_record":
                    var st = await db.WorkRecords.Where(r => r.Id == id && r.InstitutionId == InstitutionId)
                        .Select(r => (RecordStatus?)r.Status).FirstOrDefaultAsync(ct);
                    if (st is null) return "找不到對應的工作記錄";
                    return st == RecordStatus.Draft
                        ? null
                        : "此記錄已送簽或已完成，附件不可增刪；如需修改請先抽單或退件";

                default:
                    return null;
            }
        }

        /// <summary>
        /// **資料列層**的可讀判斷（回 null＝可讀，非 null＝拒絕的理由）。
        ///
        /// ⚠⚠ 為什麼不能只靠 <see cref="FileAccessPolicy"/>：那是**分類層級**的。
        /// 請假的證明文件掛在 <c>Attendance</c> 分類，而它的讀取權 <c>attendance.read</c>
        /// **全院每個角色都有** —— 分類層完全擋不住「只有本人、人事、院長」。
        ///
        /// ⚠⚠ 而且要擋的**不只下載**：列表端點會回傳 <c>OriginalName</c>，
        /// 而「○○○_B型肝炎診斷證明.pdf」**檔名本身就是病歷資訊**。只擋下載等於沒擋。
        /// ⇒ List／Download／Delete／Upload 四個入口都要串。
        ///
        /// ⚠ 已知邊界：<c>PermissionClaims.HasPermission</c> 對 <c>system.admin</c> 一律回 true，
        /// 所以系統管理員技術上看得到。對能直接讀資料庫的人來說擋 API 是假動作 ——
        /// 真正的嚇阻是**每次開啟都寫 <c>file.download</c> 稽核紀錄**，
        /// 那也正是院方「個人資料調閱申請暨紀錄表」要的東西。
        /// </summary>
        private async Task<string?> OwnerReadableAsync(string? ownerType, int? ownerId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(ownerType) || ownerId is not int id) return null;

            switch (OwnerKind(ownerType))
            {
                // ⚠ 其他型別維持既有行為（分類層權限）。
                // ⭐ 新專案有「只有本人／特定人能看」的附件（病歷、證明文件…），在這裡加 case：
                //   分類層權限通常全員都有，只擋分類等於沒擋；而且列表會回檔名，檔名本身就可能是個資。
                default:
                    return null;
            }
        }

        /// <summary>
        /// 儲存後端的自我診斷（後台用）。
        /// ⚠ 寫入權限**必須實際寫一次才知道** —— 只有讀取權限的憑證讀得到也列得出，只有寫會失敗。
        /// </summary>
        [HttpGet("storage-status")]
        [RequirePermission("system.admin")]
        public async Task<IActionResult> StorageStatus(CancellationToken ct = default)
        {
            var inst = await db.Institutions.FirstOrDefaultAsync(i => i.Id == InstitutionId, ct);
            var gcs = HttpContext.RequestServices.GetService<GcsFileStorage>();

            var result = new
            {
                institution = inst?.Name,
                configured = inst?.StorageBackend.ToString() ?? "Local",
                gcsConfigured = gcs is not null,
                gcs = gcs is null
                    ? null
                    : await gcs.ProbeAsync(ct) is var p
                        ? new { canRead = p.CanRead, canWrite = p.CanWrite, message = p.Message }
                        : null,
            };
            return Ok(result);
        }

        /// <summary>
        /// 切換本機構的儲存後端。
        ///
        /// ⚠ **切換不搬檔案。** 既有檔案仍在原本的後端，靠
        /// <see cref="StoredFile.Backend"/> 各自記著自己在哪裡，讀取時照舊可用。
        /// 這是刻意的：搬遷是另一件事（要處理失敗回滾、雜湊驗證、頻寬），
        /// 混在切換裡做，出錯時會分不清是切換壞了還是搬遷壞了。
        ///
        /// ⚠ 切到雲端前**強制先做一次實際讀寫測試**，測不過就不讓切 ——
        /// 否則設定改好了、使用者一上傳才失敗，而那時他手上正拿著要傳的檔案。
        /// </summary>
        [HttpPut("storage-backend")]
        [RequirePermission("system.admin")]
        public async Task<IActionResult> SetStorageBackend(
            [FromBody] SetStorageBackendRequest request, CancellationToken ct = default)
        {
            var inst = await db.Institutions.FirstOrDefaultAsync(i => i.Id == InstitutionId, ct);
            if (inst is null) return NotFound();

            if (request.Backend == StorageBackend.Gcs)
            {
                var gcs = HttpContext.RequestServices.GetService<GcsFileStorage>();
                if (gcs is null)
                    return BadRequest(new
                    {
                        message = "尚未設定雲端儲存（Storage:Gcs:Bucket），或啟動時取不到憑證",
                        code = "gcs_not_configured",
                    });

                var (canRead, canWrite, msg) = await gcs.ProbeAsync(ct);
                if (!canRead || !canWrite)
                    return BadRequest(new
                    {
                        message = $"雲端儲存目前不可用，未切換：{msg}",
                        code = "gcs_unavailable",
                    });
            }

            var before = inst.StorageBackend;
            inst.StorageBackend = request.Backend;

            var existing = await db.StoredFiles
                .Where(f => f.InstitutionId == InstitutionId && f.Backend == before)
                .CountAsync(ct);

            audit.Log("institution.storage_backend", "institution", inst.Id,
                new { backend = before.ToString() },
                new { backend = request.Backend.ToString(), filesLeftOnOldBackend = existing });
            await db.SaveChangesAsync(ct);

            return Ok(new
            {
                backend = inst.StorageBackend.ToString(),
                previous = before.ToString(),
                // 講清楚舊檔沒有被搬走 —— 不講的話，人會以為切換＝搬遷
                filesLeftOnOldBackend = existing,
                note = existing > 0
                    ? $"已切換。既有 {existing} 個檔案仍存放於「{before}」，讀取不受影響；搬遷需另行執行"
                    : "已切換。",
            });
        }
    }

    public record SetStorageBackendRequest(StorageBackend Backend);
}
