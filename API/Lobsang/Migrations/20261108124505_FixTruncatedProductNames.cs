using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lobsang.Migrations
{
    /// <inheritdoc />
    public partial class FixTruncatedProductNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 被截斷的商品名稱（照片清單上印不完的）改成誠品線上的完整書名，業主 2026-11-08 要求用 ISBN 到誠品取得。
            // ⭐ 只在名稱「還是當初截斷的那個」時才改（WHERE name = 舊名）：業主若已自己在後台改過，就不覆蓋。
            migrationBuilder.Sql(@"
UPDATE products SET name = '動手做模型X啟動知識力! 立體博物館系列套書: 打造你的恐龍博物館+打造你的歷史博物館立體書 (附5組史前恐龍立體模型/5組古代文物立體模型/2冊合售)', updated_at = UTC_TIMESTAMP() WHERE sku = '1104260029' AND name = '動手做模型X啟動知識力！立體博物館系列套書：打造你的恐龍博物館+打…';
UPDATE products SET name = '我們的二十四節氣 夏耘: 立夏、小滿、芒種、夏至、小暑、大暑 (附QR Code)', updated_at = UTC_TIMESTAMP() WHERE sku = '1104260032' AND name = '我們的二十四節氣 夏耘：立夏、小滿、芒種、夏至、小暑、大暑（附QR c…';
UPDATE products SET name = '我們的二十四節氣 秋收: 立秋、處暑、白露、秋分、寒露、霜降 (附QR Code)', updated_at = UTC_TIMESTAMP() WHERE sku = '1104260033' AND name = '我們的二十四節氣 秋收：立秋、處暑、白露、秋分、寒露、霜降（附QR c…';
UPDATE products SET name = '我們的二十四節氣 冬藏: 立冬、小雪、大雪、冬至、小寒、大寒 (附QR Code)', updated_at = UTC_TIMESTAMP() WHERE sku = '1104260034' AND name = '我們的二十四節氣 冬藏：立冬、小雪、大雪、冬至、小寒、大寒（附QR c…';
UPDATE products SET name = '藝術大師人物立體書套書: 梵谷+達文西+芙烈達, 驚喜多層次翻頁, 藝術家生平+經典名畫, 美學素養從小養成 (3冊合售)', updated_at = UTC_TIMESTAMP() WHERE sku = '1105260046' AND name = '藝術大師人物立體書套書：梵谷+達文西+芙烈達，驚喜多層次翻頁，藝術家…';
UPDATE products SET name = 'The Little Mermaid and Other Fairy Tales: Lavishly Illustrated with Interactive Elements', updated_at = UTC_TIMESTAMP() WHERE sku = '1206260004' AND name = 'The Little Mermaid and Other Fairy Tales: Lavishly Illustrated with In…';
UPDATE products SET name = 'The Four Seasons: Great Works of Japanese Woodblock Printing', updated_at = UTC_TIMESTAMP() WHERE sku = '1302260005' AND name = 'The Four Seasons: Great Works of Japanese Woodblock';
UPDATE products SET name = 'Cherry Blossoms in Bloom: Great Works of Japanese Woodblock Printing', updated_at = UTC_TIMESTAMP() WHERE sku = '1302260006' AND name = 'Cherry Blossoms in Bloom: Great Works of Japanese';
UPDATE products SET name = 'Flowers of Japan: Great Works of Japanese Woodblock Printing', updated_at = UTC_TIMESTAMP() WHERE sku = '1302260007' AND name = 'Flowers of Japan: Great Works of Japanese';
UPDATE products SET name = 'Begin Again, and Again, and Again: Notes on the Art of Perpetual Renewal', updated_at = UTC_TIMESTAMP() WHERE sku = '1404260006' AND name = 'Begin Again, and Again, and Again: Notes on the Art of Perpetual R…';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
