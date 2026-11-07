using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Lobsang.Data;
using Lobsang.Models;

namespace Lobsang.Services
{
    /// <summary>
    /// 檔案實體的存取。
    ///
    /// ⚠ 介面只有四個動作，刻意不含「列出目錄」——
    /// 清單一律查資料庫（<see cref="StoredFile"/>），不掃儲存區。
    /// 儲存區是內容的家，不是索引；掃描它會漏掉權限與軟刪除。
    /// </summary>
    public interface IFileStorage
    {
        StorageBackend Backend { get; }
        Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default);
        Task<Stream> OpenAsync(string key, CancellationToken ct = default);
        Task<bool> ExistsAsync(string key, CancellationToken ct = default);
        Task DeleteAsync(string key, CancellationToken ct = default);
    }

    /// <summary>路徑組裝與驗證。共用給兩種後端，避免各寫一份而規則漂移。</summary>
    public static class ObjectKeys
    {
        /// <summary>
        /// 合法的物件路徑。**只允許這個形狀** ——
        /// 這是路徑穿越（<c>../</c>）、絕對路徑與奇怪字元的唯一防線，
        /// 而且兩種後端都吃同一條規則。
        /// </summary>
        private static readonly Regex Valid = new(
            @"^inst-\d+/[a-z]+/\d{4}/\d{2}/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.[a-z0-9]{1,8}$",
            RegexOptions.Compiled);

        public static bool IsValid(string key) => Valid.IsMatch(key);

        /// <summary>
        /// 組出物件路徑。⚠ 檔名一律 GUID，原始檔名完全不參與 ——
        /// 中文檔名、同名覆蓋、<c>../</c> 都由這一刀根絕。
        /// </summary>
        public static string Build(int institutionId, FileCategory category, string extension, DateTime now)
        {
            var ext = extension.StartsWith('.') ? extension : "." + extension;
            return $"inst-{institutionId}/{category.ToString().ToLowerInvariant()}/"
                 + $"{now:yyyy}/{now:MM}/{Guid.NewGuid():D}{ext.ToLowerInvariant()}";
        }
    }

    /// <summary>
    /// 主機本機檔案系統。民生醫院用這個（目前沒有對外雲端網路），
    /// 也是 GCS 尚未開通寫入權限前的預設。
    /// </summary>
    public class LocalFileStorage(string root) : IFileStorage
    {
        public StorageBackend Backend => StorageBackend.Local;

        private string PathOf(string key)
        {
            if (!ObjectKeys.IsValid(key))
                throw new InvalidOperationException($"不合法的物件路徑：{key}");

            // ⚠ 即使格式已驗過，仍再做一次「解析後必須still在 root 底下」的檢查。
            //    格式驗證與路徑歸一化是兩件事，兩道都要。
            var full = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
            var baseDir = Path.GetFullPath(root);
            if (!full.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("物件路徑逸出儲存根目錄");
            return full;
        }

        public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
        {
            var path = PathOf(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await content.CopyToAsync(fs, ct);
        }

        public Task<Stream> OpenAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<Stream>(new FileStream(PathOf(key), FileMode.Open, FileAccess.Read, FileShare.Read));

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(File.Exists(PathOf(key)));

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            var path = PathOf(key);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 依機構決定要用哪個後端。
    ///
    /// ⚠ 這一層存在的理由：育幼院用雲端、民生醫院留本機，
    /// 但兩家跑在**同一個部署**上。把後端寫在 appsettings 的單一值，第二家就得另開部署。
    /// </summary>
    public class FileStorageResolver(AppDbContext db, IServiceProvider sp)
    {
        private readonly Dictionary<int, IFileStorage> _cache = [];

        public async Task<IFileStorage> ForAsync(int institutionId, CancellationToken ct = default)
        {
            if (_cache.TryGetValue(institutionId, out var hit)) return hit;

            var backend = await db.Institutions
                .Where(i => i.Id == institutionId)
                .Select(i => (StorageBackend?)i.StorageBackend)
                .FirstOrDefaultAsync(ct) ?? StorageBackend.Local;

            IFileStorage store = backend switch
            {
                StorageBackend.Gcs => sp.GetService<GcsFileStorage>()
                    // 設定成雲端卻沒有可用的憑證時**直接失敗**，不要默默退回本機 ——
                    // 那會讓檔案散落在兩個地方，而且沒有人會發現
                    ?? throw new InvalidOperationException(
                        "此機構設定為雲端儲存，但 GCS 尚未設定或憑證不可用"),
                _ => sp.GetRequiredService<LocalFileStorage>(),
            };

            _cache[institutionId] = store;
            return store;
        }
    }
}
