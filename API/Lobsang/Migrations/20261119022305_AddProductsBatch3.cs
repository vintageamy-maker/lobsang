using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lobsang.Migrations
{
    /// <inheritdoc />
    public partial class AddProductsBatch3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 第三批商品：業主 2026-11-19 照片 1 張（Document/提供資料/1108/20261119_101122200.JPG），共 22 筆。
            // 照片只拍到店內碼和 ISBN（名稱欄被裁掉），22 筆 ISBN 檢查碼都正確。
            // ⭐ 名稱用 ISBN 查書：誠品線上查到 18 本用誠品書名；另外 4 本誠品沒有，用 openBD／Google Books
            //   （1102260034、1202260019、1201260016、1102260037）。業主要改名稱直接在後台改。
            // ⭐ 店內碼已經存在的跳過（NOT EXISTS），不會覆蓋業主改過的資料；分類由店內碼前 4 碼對照現有分類。
            migrationBuilder.Sql(@"
INSERT INTO products (institution_id, sku, name, code, mid_category, sub_category, sale_price, stock, is_listed, authors, data_json, created_at, updated_at)
SELECT i.id, v.sku, v.name, v.code, v.mid, v.sub, NULL, 0, 1, NULL, '{}', UTC_TIMESTAMP(), UTC_TIMESTAMP()
FROM (SELECT MIN(id) AS id FROM institutions) i
JOIN (
    SELECT '1102260034' AS sku, 'パレ・ロワイヤル のぞきからくり絵本（復刻版）' AS name, '9784499282444' AS code, '1 立體書與紙藝' AS mid, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1102260035', 'ロンドン万国博覧会~1851~(復刻版)', '9784499286725', '1 立體書與紙藝', '1102 童話與文學故事立體書'
    UNION ALL SELECT '1102260036', 'モーションシルエット', '9784766128116', '1 立體書與紙藝', '1102 童話與文學故事立體書'
    UNION ALL SELECT '1202260019', 'Brooch（English version）', '9784898151686', '2 大人繪本與圖像小說', '1202 亞洲與美式名家繪本'
    UNION ALL SELECT '1201260016', 'Un livre', '9791094908457', '2 大人繪本與圖像小說', '1201 歐洲與法式名家繪本'
    UNION ALL SELECT '1102260037', 'The Forest', '9781592702183', '1 立體書與紙藝', '1102 童話與文學故事立體書'
    UNION ALL SELECT '1105260050', 'Veggie et ses Amis', '9782361936068', '1 立體書與紙藝', '1105 藝術、品牌與休閒娛樂立體書'
    UNION ALL SELECT '1105260051', 'Un Éléphant dans un Palais de Porcelaine', '9782361935580', '1 立體書與紙藝', '1105 藝術、品牌與休閒娛樂立體書'
    UNION ALL SELECT '1105260052', 'Funny Birds', '9783791371474', '1 立體書與紙藝', '1105 藝術、品牌與休閒娛樂立體書'
    UNION ALL SELECT '1105260053', 'Carolles', '9782361935375', '1 立體書與紙藝', '1105 藝術、品牌與休閒娛樂立體書'
    UNION ALL SELECT '1105260054', 'Robopop', '9782361935306', '1 立體書與紙藝', '1105 藝術、品牌與休閒娛樂立體書'
    UNION ALL SELECT '1105260055', 'Tout Au Fond', '9782361934637', '1 立體書與紙藝', '1105 藝術、品牌與休閒娛樂立體書'
    UNION ALL SELECT '1102260038', 'Le Petit Chaperon Rouge', '9782361935535', '1 立體書與紙藝', '1102 童話與文學故事立體書'
    UNION ALL SELECT '1102260039', 'Les Dix Droits du Lecteur', '9782070649143', '1 立體書與紙藝', '1102 童話與文學故事立體書'
    UNION ALL SELECT '1104260055', 'Jungle', '9782075101073', '1 立體書與紙藝', '1104 科普與知識類立體書'
    UNION ALL SELECT '1102260040', 'Bulles de Savon', '9782361933982', '1 立體書與紙藝', '1102 童話與文學故事立體書'
    UNION ALL SELECT '1104260056', 'ABC en Relief', '9782361933906', '1 立體書與紙藝', '1104 科普與知識類立體書'
    UNION ALL SELECT '1102260041', 'Moby-Dick: A Pop-Up Book from the Novel by Herman Melville', '9781452173849', '1 立體書與紙藝', '1102 童話與文學故事立體書'
    UNION ALL SELECT '1104260057', 'Là où tout est blanc', '9782361936112', '1 立體書與紙藝', '1104 科普與知識類立體書'
    UNION ALL SELECT '1104260058', 'Paper Blossoms: A Book of Beautiful Bouquets for the Table', '9780811874199', '1 立體書與紙藝', '1104 科普與知識類立體書'
    UNION ALL SELECT '1104260059', 'Paper Blossoms, Butterflies & Birds: A Book of Beautiful Bouquets for the Table', '9781452113913', '1 立體書與紙藝', '1104 科普與知識類立體書'
    UNION ALL SELECT '1105260056', 'The Sleeping Beauty Theatre: Put on Your Own Show', '9780500650547', '1 立體書與紙藝', '1105 藝術、品牌與休閒娛樂立體書'
) v ON 1 = 1
WHERE i.id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM products p WHERE p.institution_id = i.id AND p.sku = v.sku);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
