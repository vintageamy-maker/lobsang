using Google;
using Google.Cloud.Storage.V1;
using Lobsang.Models;

namespace Lobsang.Services
{
    /// <summary>
    /// Google Cloud Storage 後端（育幼院）。
    ///
    /// 認證走 **Application Default Credentials**：在 GCE 上就是 VM 的服務帳戶，
    /// 不放金鑰檔（本專案所在的組織已用 <c>iam.disableServiceAccountKeyCreation</c>
    /// 政策禁止產生長期金鑰，那是好的預設）。
    ///
    /// ⚠ 因此 VM 的存取範圍必須含 <c>devstorage.read_write</c>；
    /// 只有 <c>read_only</c> 時寫入會回 403「Provided scope(s) are not authorized」。
    /// 變更存取範圍需停機重開 VM，故此後端可能在憑證到位前就先部署 ——
    /// <see cref="ProbeAsync"/> 就是給啟動檢查與後台診斷用的。
    /// </summary>
    public class GcsFileStorage : IFileStorage
    {
        private readonly StorageClient _client;
        private readonly string _bucket;

        public GcsFileStorage(StorageClient client, string bucket)
        {
            _client = client;
            _bucket = bucket;
        }

        public StorageBackend Backend => StorageBackend.Gcs;

        public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
        {
            if (!ObjectKeys.IsValid(key))
                throw new InvalidOperationException($"不合法的物件路徑：{key}");

            await _client.UploadObjectAsync(
                _bucket, key, contentType, content,
                // ⚠ IfGenerationMatch = 0 → 只在物件**不存在**時才寫入。
                //    路徑帶 GUID 本來就不該撞，但真的撞了要炸出來，不能默默覆蓋別人的檔案。
                new UploadObjectOptions { IfGenerationMatch = 0 },
                cancellationToken: ct);
        }

        public async Task<Stream> OpenAsync(string key, CancellationToken ct = default)
        {
            if (!ObjectKeys.IsValid(key))
                throw new InvalidOperationException($"不合法的物件路徑：{key}");

            var ms = new MemoryStream();
            await _client.DownloadObjectAsync(_bucket, key, ms, cancellationToken: ct);
            ms.Position = 0;
            return ms;
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            if (!ObjectKeys.IsValid(key)) return false;
            try
            {
                await _client.GetObjectAsync(_bucket, key, cancellationToken: ct);
                return true;
            }
            catch (GoogleApiException e) when (e.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        public async Task DeleteAsync(string key, CancellationToken ct = default)
        {
            if (!ObjectKeys.IsValid(key))
                throw new InvalidOperationException($"不合法的物件路徑：{key}");
            try
            {
                await _client.DeleteObjectAsync(_bucket, key, cancellationToken: ct);
            }
            catch (GoogleApiException e) when (e.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // 已經不在了就當作成功 —— 刪除要可重入
            }
        }

        /// <summary>
        /// 連線與權限自我診斷。回傳的訊息是給人看的，會直接顯示在後台。
        /// 寫入權限**必須實際寫一次才知道**：read_only 的憑證讀得到、列得出，只有寫會失敗。
        /// </summary>
        public async Task<(bool CanRead, bool CanWrite, string Message)> ProbeAsync(CancellationToken ct = default)
        {
            var canRead = false;
            try
            {
                await foreach (var _ in _client.ListObjectsAsync(_bucket, "", new ListObjectsOptions { PageSize = 1 })
                                   .WithCancellation(ct))
                    break;
                canRead = true;
            }
            catch (Exception e)
            {
                return (false, false, $"讀取失敗：{e.Message}");
            }

            var probeKey = $"_probe/{Guid.NewGuid():N}.txt";
            try
            {
                using var ms = new MemoryStream("probe"u8.ToArray());
                await _client.UploadObjectAsync(_bucket, probeKey, "text/plain", ms, cancellationToken: ct);
                await _client.DeleteObjectAsync(_bucket, probeKey, cancellationToken: ct);
                return (true, true, "讀寫皆正常");
            }
            catch (GoogleApiException e) when (e.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return (canRead, false,
                    "可讀但**不可寫**。多半是 VM 的存取範圍仍為 devstorage.read_only —— "
                    + "需將執行個體的存取範圍改為 read_write（需停機重開），"
                    + $"或改用具 roles/storage.objectAdmin 的服務帳戶。原始訊息：{e.Message}");
            }
            catch (Exception e)
            {
                return (canRead, false, $"寫入失敗：{e.Message}");
            }
        }
    }
}
