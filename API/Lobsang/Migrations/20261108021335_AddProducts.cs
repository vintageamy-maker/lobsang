using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lobsang.Migrations
{
    /// <inheritdoc />
    public partial class AddProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    institution_id = table.Column<int>(type: "int", nullable: false),
                    sku = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mid_category = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sub_category = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sale_price = table.Column<int>(type: "int", nullable: true),
                    stock = table.Column<int>(type: "int", nullable: false),
                    is_listed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    authors = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    data_json = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<int>(type: "int", nullable: true),
                    updated_by = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_products_code",
                table: "products",
                column: "code");

            migrationBuilder.CreateIndex(
                name: "ix_products_institution_id_sku",
                table: "products",
                columns: new[] { "institution_id", "sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_name",
                table: "products",
                column: "name");

            // 初始資料：業主 2026-11-08 照片清單 40 筆（Document/提供資料/1108，ISBN 已驗檢查碼）。
            // ⭐ 放在 migration 裡 ⇒ 每個資料庫只會跑一次；業主之後刪掉也不會被種回來（DbSeeder 每次啟動都跑，不適合）。
            // ⚠ 機構取最小的 id；全新的空資料庫（還沒有機構）就不放，等業主自己匯入。
            migrationBuilder.Sql(@"
INSERT INTO products (institution_id, sku, name, code, mid_category, sub_category, sale_price, stock, is_listed, authors, data_json, created_at, updated_at)
SELECT i.id, v.sku, v.name, v.code, '1 立體書與紙藝', v.sub, NULL, 0, 1, NULL, '{}', UTC_TIMESTAMP(), UTC_TIMESTAMP()
FROM (SELECT MIN(id) AS id FROM institutions) i
JOIN (
    SELECT '1101260001' AS sku, 'The Elements of Pop-Up' AS name, '9780689822247' AS code, '1101 紙藝手作與技術理論' AS sub
    UNION ALL SELECT '1101260004' AS sku, '一刀入魂，鏤空紙雕的立體美雕塑（暢銷版）' AS name, '9789867627124' AS code, '1101 紙藝手作與技術理論' AS sub
    UNION ALL SELECT '1101260006' AS sku, '立體書不可思議' AS name, '9789571363516' AS code, '1101 紙藝手作與技術理論' AS sub
    UNION ALL SELECT '1101260007' AS sku, '立體書的異想世界' AS name, '9789868802520' AS code, '1101 紙藝手作與技術理論' AS sub
    UNION ALL SELECT '1101260008' AS sku, '立體書的異想世界' AS name, '9789868802537' AS code, '1101 紙藝手作與技術理論' AS sub
    UNION ALL SELECT '1101260003' AS sku, '立體書創作手冊（8冊合售）' AS name, '9789869791106' AS code, '1101 紙藝手作與技術理論' AS sub
    UNION ALL SELECT '1101260005' AS sku, '自己作收到會開心一笑の立體卡片' AS name, '9789865724047' AS code, '1101 紙藝手作與技術理論' AS sub
    UNION ALL SELECT '1101260002' AS sku, '紙雕創作的擬真風景：紙雕技術大解析' AS name, '9789579559928' AS code, '1101 紙藝手作與技術理論' AS sub
    UNION ALL SELECT '1102260019' AS sku, 'A Year in the Moominhouse: A Carousel Book of the Seasons' AS name, '9781509853595' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260024' AS sku, 'Alice''s Adventures in Wonderland: A Pop-up Adaptation' AS name, '9780689847431' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260015' AS sku, 'Beauty & the Beast' AS name, '9781416960799' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260016' AS sku, 'Cinderella: A Pop-Up Fairy Tale' AS name, '9781416905011' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260030' AS sku, 'Cinderella: A Three Dimensional Fairy Tale Theatre' AS name, '9781406377842' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260011' AS sku, 'Disney Princess: A Magical Pop-Up World' AS name, '9781608875535' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260010' AS sku, 'Disney Princess: The Little Mermaid Pop-Up Book' AS name, '9781647227593' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260009' AS sku, 'Disney: Cinderella Pop-Up Book' AS name, '9798886636185' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260008' AS sku, 'Famille Acrobate' AS name, '9782330106645' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260012' AS sku, 'Harry Potter''s Journey to Hogwarts: A Book of Magical Surprises' AS name, '9798886635096' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260004' AS sku, 'Le Cerisier de Grand-Père' AS name, '9782361936075' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260007' AS sku, 'Le Lutin Bleu' AS name, '9782330086237' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260001' AS sku, 'Le Petit Prince: Le Grand Livre Pop-up' AS name, '9782070667222' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260002' AS sku, 'Le Petit Prince: un livre carrousel' AS name, '9782075158954' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260025' AS sku, 'Little Fish: A Carousel Book' AS name, '9780500651629' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260003' AS sku, 'Mécanique Générale' AS name, '9782361935573' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260022' AS sku, 'Peter Pan: A Classic Collectible Pop-Up' AS name, '9780689853647' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260028' AS sku, 'Sleeping Beauty: An Enchanting Pop-up Classic' AS name, '9781800788114' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260027' AS sku, 'Swan Lake: An Enchanting Pop-up Classic' AS name, '9781800787278' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260029' AS sku, 'The Animal Fair Pop-up' AS name, '9781406396997' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260006' AS sku, 'The Brambly Hedge Pop-Up Book' AS name, '9780008547110' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260018' AS sku, 'The Jungle Book: A Pop-Up Adventure' AS name, '9781416918240' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260017' AS sku, 'The Little Mermaid' AS name, '9781416960805' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260005' AS sku, 'The Little Prince (Pop-up Ed.)' AS name, '9780008595227' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260014' AS sku, 'The Mitten: A Classic Pop-Up Folktale' AS name, '9781623481537' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260020' AS sku, 'The Moomins Find a Home: A Pop-Up Adventure' AS name, '9781035038404' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260026' AS sku, 'The Nutcracker: Zanna Goldhawk' AS name, '9781800783614' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260021' AS sku, 'The Snowman Pop-Up' AS name, '9780241418932' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260023' AS sku, 'The Wonderful Wizard of Oz: A Commemorative Pop-up' AS name, '9780689817519' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260013' AS sku, 'Wicked: The Story of Oz & the Wonderful Wizard Replica Pop-Up Book' AS name, '9798886638127' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260033' AS sku, '小王子立體書' AS name, '9786263741225' AS code, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260031' AS sku, '走進夢幻童話：立體光影繪本' AS name, '9786267295571' AS code, '1102 童話與文學故事立體書' AS sub
) v ON 1 = 1
WHERE i.id IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "products");
        }
    }
}
