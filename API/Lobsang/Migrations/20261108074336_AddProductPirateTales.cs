using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lobsang.Migrations
{
    /// <inheritdoc />
    public partial class AddProductPirateTales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 第二批照片漏掉的 1 筆：清單印的 ISBN 9783836584476 檢查碼不對，業主 2026-11-08 給正確的 9783836584760。
            // ⭐ 店內碼已經存在就跳過（業主若已自己在後台新增，不會重複）；分類照店內碼 1303。
            migrationBuilder.Sql(@"
INSERT INTO products (institution_id, sku, name, code, mid_category, sub_category, sale_price, stock, is_listed, authors, data_json, created_at, updated_at)
SELECT i.id, '1303260009', 'Pirate Tales', '9783836584760', '3 藝術、設計與視覺', '1303 平面設計與文字美學', NULL, 0, 1, NULL, '{}', UTC_TIMESTAMP(), UTC_TIMESTAMP()
FROM (SELECT MIN(id) AS id FROM institutions) i
WHERE i.id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM products p WHERE p.institution_id = i.id AND p.sku = '1303260009');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
