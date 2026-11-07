using SkiaSharp;

namespace Lobsang.Services
{
    /// <summary>
    /// 上傳照片先縮小再存（2026-09-28 業主：出餐照片載很久，原圖平均 3.1 MB）。
    ///
    /// ⭐ 規則：最長邊超過 <see cref="MaxEdge"/> 就等比縮到 <see cref="MaxEdge"/>，一律存成 JPEG。
    ///   手機全螢幕看、列印 A4 半頁都夠清楚；一張約 0.3～0.5 MB。
    /// ⚠ 原圖**不保留**（業主已同意）。
    /// ⚠ 縮不了就原樣存，**絕不因為縮圖失敗而擋掉上傳**：
    ///   HEIC 在 Linux 的 Skia 解不開、壞檔、極端尺寸……都走原樣。
    /// ⚠ 縮完反而比較大（例如本來就壓得很小的圖）也用原檔。
    /// </summary>
    public static class ImageShrinker
    {
        public const int MaxEdge = 1600;
        public const int JpegQuality = 80;

        /// <summary>已經夠小就不重壓：邊長在上限內而且小於這個大小</summary>
        private const long SmallEnough = 600 * 1024;

        /// <summary>解碼前先擋：像素數太誇張的圖（惡意或掃描超大圖）不處理，避免吃光記憶體</summary>
        private const long MaxPixels = 100_000_000;

        private static readonly HashSet<string> Shrinkable = ["image/jpeg", "image/png", "image/webp"];

        public record Result(byte[] Bytes, int Width, int Height);

        /// <summary>回傳縮好的 JPEG；不需要或做不到時回傳 null（呼叫端用原檔）</summary>
        public static Result? TryShrink(byte[] input, string contentType)
        {
            if (!Shrinkable.Contains(contentType)) return null;
            try
            {
                using var data = SKData.CreateCopy(input);
                using var codec = SKCodec.Create(data);
                if (codec is null) return null;

                var w = codec.Info.Width;
                var h = codec.Info.Height;
                if (w <= 0 || h <= 0 || (long)w * h > MaxPixels) return null;
                if (Math.Max(w, h) <= MaxEdge && input.LongLength <= SmallEnough) return null;

                using var src = SKBitmap.Decode(codec);
                if (src is null) return null;

                var scale = Math.Min(1.0, (double)MaxEdge / Math.Max(w, h));
                var sw = Math.Max(1, (int)Math.Round(w * scale));
                var sh = Math.Max(1, (int)Math.Round(h * scale));

                using var scaled = scale < 1.0
                    ? src.Resize(new SKImageInfo(sw, sh, SKColorType.Rgba8888, SKAlphaType.Premul), SKFilterQuality.High)
                    : src.Copy();
                if (scaled is null) return null;

                // ⚠ 手機照片的「直拍」多半是橫的像素＋EXIF 方向標記；
                //   重新編碼會丟掉 EXIF，所以要在這裡把方向真的轉過來，否則照片會躺著。
                var origin = codec.EncodedOrigin;
                var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                                  or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
                var ow = swap ? sh : sw;
                var oh = swap ? sw : sh;

                using var surface = SKSurface.Create(new SKImageInfo(ow, oh, SKColorType.Rgba8888, SKAlphaType.Premul));
                if (surface is null) return null;
                var canvas = surface.Canvas;
                // JPEG 沒有透明：PNG 的透明底鋪白，不然會變黑
                canvas.Clear(SKColors.White);
                canvas.SetMatrix(OrientationMatrix(origin, sw, sh));
                canvas.DrawBitmap(scaled, 0, 0);
                canvas.Flush();

                using var image = surface.Snapshot();
                using var jpg = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
                if (jpg is null) return null;
                var bytes = jpg.ToArray();
                if (bytes.LongLength >= input.LongLength) return null;
                return new Result(bytes, ow, oh);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把「未轉向的像素（寬 w、高 h）」畫成正確方向用的矩陣</summary>
        private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity,
        };

        /// <summary>檔名副檔名跟著換成 .jpg（下載時才不會拿到「內容是 JPEG、檔名叫 .png」）</summary>
        public static string JpgName(string originalName)
        {
            var stem = Path.GetFileNameWithoutExtension(originalName);
            return (string.IsNullOrWhiteSpace(stem) ? "photo" : stem) + ".jpg";
        }
    }
}
