using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lobsang.Migrations
{
    /// <inheritdoc />
    public partial class AddProductsBatch2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 第二批商品：業主 2026-11-08 照片 8 張（Document/提供資料/1108，74144～74151），共 323 筆，
            // ISBN 檢查碼不對的 1 筆（1303260009 Pirate Tales，印的是 9783836584476）先不放，等業主確認。
            // ⭐ 店內碼已經存在的跳過（NOT EXISTS），不會覆蓋業主改過的資料；每個資料庫只跑一次。
            // ⭐ 分類由店內碼前 2／4 碼對照清單上的中分類、子分類（照片右欄因紙張彎曲對不齊，以店內碼為準）。
            migrationBuilder.Sql(@"
INSERT INTO products (institution_id, sku, name, code, mid_category, sub_category, sale_price, stock, is_listed, authors, data_json, created_at, updated_at)
SELECT i.id, v.sku, v.name, v.code, v.mid, v.sub, NULL, 0, 1, NULL, '{}', UTC_TIMESTAMP(), UTC_TIMESTAMP()
FROM (SELECT MIN(id) AS id FROM institutions) i
JOIN (
    SELECT '1102260032' AS sku, '故宮彈起：紙上探秘紫禁城' AS name, '9789620886874' AS code, '1 立體書與紙藝' AS mid, '1102 童話與文學故事立體書' AS sub
    UNION ALL SELECT '1103260003' AS sku, 'Flurry: A Mini Snowflakes Pop-Up Book' AS name, '9781623486532' AS code, '1 立體書與紙藝' AS mid, '1103 神話與傳統節慶立體書' AS sub
    UNION ALL SELECT '1103260004' AS sku, 'The Dragon & the Knight: A Pop-Up Misadventure' AS name, '9781416960812' AS code, '1 立體書與紙藝' AS mid, '1103 神話與傳統節慶立體書' AS sub
    UNION ALL SELECT '1103260005' AS sku, 'The First Noel' AS name, '9781406383317' AS code, '1 立體書與紙藝' AS mid, '1103 神話與傳統節慶立體書' AS sub
    UNION ALL SELECT '1103260002' AS sku, 'The Nightmare Before Christmas: Advent Calendar and Pop-Up Book' AS name, '9781683839682' AS code, '1 立體書與紙藝' AS mid, '1103 神話與傳統節慶立體書' AS sub
    UNION ALL SELECT '1103260001' AS sku, 'Welcome to the Museum: A Christmas Pop-Up Advent Calendar' AS name, '9781800784369' AS code, '1 立體書與紙藝' AS mid, '1103 神話與傳統節慶立體書' AS sub
    UNION ALL SELECT '1103260010' AS sku, '三國演義（立體珍藏版）' AS name, '9786267160787' AS code, '1 立體書與紙藝' AS mid, '1103 神話與傳統節慶立體書' AS sub
    UNION ALL SELECT '1103260007' AS sku, '古典神話立體書：傳說封神演義（附QR code）' AS name, '9786267482056' AS code, '1 立體書與紙藝' AS mid, '1103 神話與傳統節慶立體書' AS sub
    UNION ALL SELECT '1103260006' AS sku, '時空旅行出發囉！神話世界' AS name, '9786267482230' AS code, '1 立體書與紙藝' AS mid, '1103 神話與傳統節慶立體書' AS sub
    UNION ALL SELECT '1103260008' AS sku, '時空旅行出發囉！傳統節慶' AS name, '9786267482223' AS code, '1 立體書與紙藝' AS mid, '1103 神話與傳統節慶立體書' AS sub
    UNION ALL SELECT '1103260011' AS sku, '團圓賀新年' AS name, '9789862238028' AS code, '1 立體書與紙藝' AS mid, '1103 神話與傳統節慶立體書' AS sub
    UNION ALL SELECT '1103260009' AS sku, '歡樂過新年（全新立體版）' AS name, '9786267482384' AS code, '1 立體書與紙藝' AS mid, '1103 神話與傳統節慶立體書' AS sub
    UNION ALL SELECT '1104260009' AS sku, 'A Sea Voyage' AS name, '9780500650882' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260012' AS sku, 'A Train Journey' AS name, '9780500651827' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260016' AS sku, 'Beautiful Bugs: Nature Pop-ups' AS name, '9781664351981' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260007' AS sku, 'Bicycles: Pop-Up-Book' AS name, '9783791375618' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260003' AS sku, 'Dinosaurs: A Pop-Up Book' AS name, '9781916781085' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260021' AS sku, 'Encyclopedia Prehistorica: Dinosaurs' AS name, '9780744586909' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260022' AS sku, 'Encyclopedia Prehistorica: Sharks and Other Monsters' AS name, '9780744586893' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260017' AS sku, 'Great Britain: A Three-Dimensional Expanding Country Guide' AS name, '9781406356236' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260020' AS sku, 'London: A Three-Dimensional Expanding City Skyline' AS name, '9781406323481' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260018' AS sku, 'My First Pop-Up Mythological Monsters' AS name, '9781406392371' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260002' AS sku, 'Nature Pop-ups 2: World of Birds' AS name, '9781838916398' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260006' AS sku, 'Planet Earth: Life on Earth' AS name, '9781912944996' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260011' AS sku, 'Pop-Up Earth' AS name, '9780500652572' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260014' AS sku, 'Pop-Up Forest' AS name, '9780500653081' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260010' AS sku, 'Pop-Up Moon' AS name, '9780500651865' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260013' AS sku, 'Pop-Up Volcano' AS name, '9780500652220' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260004' AS sku, 'Safari Animals: A Pop-Up Book' AS name, '9781916781078' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260005' AS sku, 'Seas & Oceans: A Magical Pop-Up Book' AS name, '9781916781184' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260015' AS sku, 'The Incredible Pop-up Bug: With 20 Flaps to Lift and Giant Pop-ups' AS name, '9781800784130' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260019' AS sku, 'The Queen: Panorama Pops' AS name, '9781529507300' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260008' AS sku, 'The Weather: Pop-up Book' AS name, '9783791373935' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260001' AS sku, 'Wild Animals' AS name, '9791039552189' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260046' AS sku, '一步一步回到神秘恐龍時代' AS name, '9786267689745' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260028' AS sku, '二十四節氣立體遊戲書：24個節氣+29個互動機關' AS name, '9786267230565' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260036' AS sku, '大知識大百科3D立體書：大太空' AS name, '4715006460340' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260037' AS sku, '大知識大百科3D立體書：大交通' AS name, '4715006460333' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260039' AS sku, '大知識大百科3D立體書：大地下' AS name, '4715006460364' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260035' AS sku, '大知識大百科3D立體書：大恐龍' AS name, '4715006460326' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260038' AS sku, '大知識大百科3D立體書：大海洋' AS name, '4715006460357' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260045' AS sku, '小小知識家動手大發現：人體Amazing!' AS name, '9786267482636' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260047' AS sku, '小小知識家動手大發現：太空人Amazing!' AS name, '9786267160473' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260041' AS sku, '小小知識家動手大發現：法老Amazing!' AS name, '9786267314371' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260042' AS sku, '小小知識家動手大發現：消防員Amazing!' AS name, '9786267416419' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260049' AS sku, '出發吧，海洋號！一起探索世界海洋' AS name, '9789862745786' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260054' AS sku, '台灣地圖立體書遊戲套組' AS name, '4711132389562' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260030' AS sku, '打造你的恐龍博物館（附5組史前恐龍立體模型）' AS name, '9789869706735' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260026' AS sku, '地球探索大發現' AS name, '9789577626752' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260048' AS sku, '地震！別怕！家庭必備的防災互動書（DIY組裝版）' AS name, '2682827250004' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260034' AS sku, '我們的二十四節氣 冬藏：立冬、小雪、大雪、冬至、小寒、大寒（附QR c…' AS name, '9789862438893' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260033' AS sku, '我們的二十四節氣 秋收：立秋、處暑、白露、秋分、寒露、霜降（附QR c…' AS name, '9789862438886' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260032' AS sku, '我們的二十四節氣 夏耘：立夏、小滿、芒種、夏至、小暑、大暑（附QR c…' AS name, '9789862438879' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260027' AS sku, '兒童衛教啟蒙：遠離病毒不生病！立體遊戲書' AS name, '9789860725469' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260051' AS sku, '法布爾昆蟲記立體書' AS name, '9786263747937' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260025' AS sku, '城市探索大發現' AS name, '9789577626257' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260040' AS sku, '時空旅行出發囉！天工開物' AS name, '9786267482216' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260043' AS sku, '砰！立體教室大發現：地球原來長這樣' AS name, '9786267160633' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260044' AS sku, '砰！立體教室大發現：海洋原來長這樣' AS name, '9786267314036' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260029' AS sku, '動手做模型X啟動知識力！立體博物館系列套書：打造你的恐龍博物館+打…' AS name, '9786267038154' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260053' AS sku, '甜點的立體世界（附高質感霧膜保護書盒）' AS name, '9789864404223' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260023' AS sku, '陸海空交通工具遊戲書' AS name, '9789577625663' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260024' AS sku, '陸海空建築工程大集合' AS name, '9789577625687' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260050' AS sku, '會動的大恐龍百科立體書' AS name, '9789862237120' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260052' AS sku, '漫遊太空立體書' AS name, '9789620872174' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1104260031' AS sku, '歡迎光臨！動物的家立體書' AS name, '9789579529167' AS code, '1 立體書與紙藝' AS mid, '1104 科普與知識類立體書' AS sub
    UNION ALL SELECT '1105260019' AS sku, '10 Autres Chaises' AS name, '9782361936440' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260017' AS sku, '10 chairs' AS name, '9782361935801' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260016' AS sku, '5 Animaux d''artistes' AS name, '9782361936105' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260018' AS sku, '5 Maisons' AS name, '9782361934736' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260027' AS sku, 'Bauhaus Ballet' AS name, '9781786274885' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260007' AS sku, 'Believe: A Pop-Up Book of Possibilities' AS name, '9780763663971' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260003' AS sku, 'DC Super Heroes: The Ultimate Pop-Up Book' AS name, '9781419769450' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260023' AS sku, 'Disneyland: Pop-Up Park Tour' AS name, '9798886630244' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260010' AS sku, 'Fashion Unfolded: Pop-Up Chanel' AS name, '9789881655028' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260011' AS sku, 'Fashion Unfolded: Pop-Up Dior' AS name, '9789881655042' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260009' AS sku, 'Fashion Unfolded: Pop-Up Louis Vuitton' AS name, '9789881655080' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260032' AS sku, 'Frida Kahlo: A Pop-Up Book' AS name, '9781916781153' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260024' AS sku, 'Godzilla: The Official Pop-Up Book' AS name, '9798886636161' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260034' AS sku, 'Henri Matisse: A Pop-Up Book' AS name, '9781916781214' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260002' AS sku, 'Here Comes Charlie Brown!' AS name, '9781419757785' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260004' AS sku, 'Hermès Pop Up' AS name, '9782330106485' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260030' AS sku, 'Hilma af Klint: A Pop-Up Book' AS name, '9781916781245' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260001' AS sku, 'I Love You: A Pop-Up Book' AS name, '9781419727344' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260041' AS sku, 'Inventions: Pop-up Models from the Drawings of Leonardo da Vinci' AS name, '9781406318289' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260006' AS sku, 'J. K. Rowling''s Wizarding World: A Pop-Up Gallery of Curiosities' AS name, '9781408885246' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260035' AS sku, 'Jane Goodall: A Pop-Up Book' AS name, '9781916781177' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260012' AS sku, 'Jardin Bleu' AS name, '9782075079822' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260033' AS sku, 'Leonardo da Vinci: A Pop-Up Book' AS name, '9781916781146' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260013' AS sku, 'Loups: Livre Pop-Up' AS name, '9782075195386' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260014' AS sku, 'Louvre Pop up (Version anglaise)' AS name, '9782361937492' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260040' AS sku, 'Love: A Pop-up Celebration' AS name, '9781406395709' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260036' AS sku, 'Mondrian: Pop-Up・Monumental' AS name, '9782358321860' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260037' AS sku, 'Monet: the Pop-up Book' AS name, '9783791377476' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260038' AS sku, 'Move!' AS name, '9780500653425' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260029' AS sku, 'Mozart: A Pop-Up Book' AS name, '9781916781320' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260020' AS sku, 'Muséum Pop up: Visite au Jardin des Plantes de Paris' AS name, '9782361935672' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260005' AS sku, 'My Cup of Art' AS name, '9788000065946' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260039' AS sku, 'New York: A Pop-up Book' AS name, '9780789339782' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260025' AS sku, 'One Red Dot' AS name, '9780689877698' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260008' AS sku, 'Paper Blossoms, for All Seasons' AS name, '9781452149981' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260015' AS sku, 'Paris Pop Up' AS name, '9782361934088' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260021' AS sku, 'Paris: 6 Cartes Pop-up' AS name, '9782745961303' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260028' AS sku, 'Paris: A Pop-up Stroll Through the City of Light' AS name, '9782408033293' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260022' AS sku, 'The Nightmare Before Christmas: Pop-Up Holiday Worlds' AS name, '9798886634037' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260031' AS sku, 'Vincent Van Gogh: A Pop-Up Book' AS name, '9781916781139' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260026' AS sku, 'White Noise' AS name, '9781416940944' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260049' AS sku, 'Wow全疊打囉！' AS name, '9786267689622' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260048' AS sku, '一步一步認識猿來的你' AS name, '9786267482377' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260047' AS sku, '山達里諾的四季（附限量DIY立體卡片）' AS name, '9786267379165' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260045' AS sku, '打造你的歷史博物館立體書' AS name, '9789579529358' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260044' AS sku, '芙烈達：人物立體書' AS name, '9786267352878' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260042' AS sku, '梵谷：人物立體書' AS name, '9786267352892' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260043' AS sku, '達文西：人物立體書' AS name, '9786267352885' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1105260046' AS sku, '藝術大師人物立體書套書：梵谷+達文西+芙烈達，驚喜多層次翻頁，藝術家…' AS name, '4717702129859' AS code, '1 立體書與紙藝' AS mid, '1105 藝術、品牌與休閒娛樂立體書' AS sub
    UNION ALL SELECT '1109260001' AS sku, '3D世界地圖小百科：動物星球大冒險（附立體地球小書）' AS name, '4715006460302' AS code, '1 立體書與紙藝' AS mid, '1109 其他立體與拉拉書' AS sub
    UNION ALL SELECT '1201260006' AS sku, 'Dehors dedans' AS name, '9782919440207' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1201260013' AS sku, 'Fort Extravagant' AS name, '9782361934385' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1201260012' AS sku, 'Inside the Villains' AS name, '9781776571987' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1201260015' AS sku, 'Jacominus Gainsborough: With Love' AS name, '9781836270522' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1201260003' AS sku, 'Je t''offre Paris... Parce que je t''aime' AS name, '9782919440191' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1201260002' AS sku, 'La chambre forêt' AS name, '9782919440924' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1201260007' AS sku, 'La danseuse' AS name, '9782919440764' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1201260001' AS sku, 'La Fragilité des passerelles (Nouvelle Ed.)' AS name, '9782919440740' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1201260010' AS sku, 'Les riches heures de Jacominus Gainsborough' AS name, '9782377310173' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1201260011' AS sku, 'Midi Pile: Une Aventure De Jacominus Gainsborough' AS name, '9782377312184' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1201260004' AS sku, 'S''il n''y avait qu''un mot... il resterait une île' AS name, '9782919440627' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1201260014' AS sku, 'The Ordinary Life of Jacominus Gainsborough' AS name, '9781836270171' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1201260005' AS sku, 'Toit et mois' AS name, '9782919440290' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1201260009' AS sku, 'Une chose formidable (+CD)' AS name, '9791040803560' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1201260008' AS sku, 'Une Toute Petite Seconde' AS name, '9782377316717' AS code, '2 大人繪本與圖像小說' AS mid, '1201 歐洲與法式名家繪本' AS sub
    UNION ALL SELECT '1202260010' AS sku, 'Ask Me' AS name, '9780547733944' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260006' AS sku, 'Frederick (Reissue Ed.)' AS name, '9780399555527' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260011' AS sku, 'Hello Lighthouse' AS name, '9780316362382' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260016' AS sku, 'I Talk Like a River' AS name, '9781529502817' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260009' AS sku, 'In the Night Kitchen' AS name, '9780064434362' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260004' AS sku, 'Lines' AS name, '9781452156651' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260013' AS sku, 'Louise Bourgeois Made Giant Spiders and Wasn''t Sorry.' AS name, '9781838666248' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260012' AS sku, 'Moomin Pull-Out Prints: Tove Jansson''s Art & Pictures' AS name, '9781529054118' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260007' AS sku, 'Nutshell Library (4冊合售)' AS name, '9780060255008' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260003' AS sku, 'Shadow' AS name, '9780811872805' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260015' AS sku, 'Small in the City' AS name, '9781406392982' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260005' AS sku, 'Swimmy' AS name, '9780399555503' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260008' AS sku, 'Ten Little Rabbits' AS name, '9780062644671' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260014' AS sku, 'Town Is by the Sea' AS name, '9781406378863' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260002' AS sku, 'Wave' AS name, '9780811859240' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260001' AS sku, 'Who Knows the Way to Mr. Grimly''s Classroom?' AS name, '9781536245011' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260017' AS sku, 'ギュスターヴくんとまぼろしのどうぶつ' AS name, '9784592763598' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1202260018' AS sku, '町田尚子画集：隙あらば猫' AS name, '9784861528972' AS code, '2 大人繪本與圖像小說' AS mid, '1202 亞洲與美式名家繪本' AS sub
    UNION ALL SELECT '1203260001' AS sku, 'A Stitch Out of Time' AS name, '9789390037025' AS code, '2 大人繪本與圖像小說' AS mid, '1203 跨國獨立與工藝手工書' AS sub
    UNION ALL SELECT '1203260003' AS sku, 'Knock! Knock!' AS name, '9789383145324' AS code, '2 大人繪本與圖像小說' AS mid, '1203 跨國獨立與工藝手工書' AS sub
    UNION ALL SELECT '1203260002' AS sku, 'Little Girls Are Wiser Than Men' AS name, '9789390037001' AS code, '2 大人繪本與圖像小說' AS mid, '1203 跨國獨立與工藝手工書' AS sub
    UNION ALL SELECT '1203260004' AS sku, 'Seed' AS name, '9789390037056' AS code, '2 大人繪本與圖像小說' AS mid, '1203 跨國獨立與工藝手工書' AS sub
    UNION ALL SELECT '1203260005' AS sku, 'Sleeping Beauty' AS name, '9781849768931' AS code, '2 大人繪本與圖像小說' AS mid, '1203 跨國獨立與工藝手工書' AS sub
    UNION ALL SELECT '1204260003' AS sku, 'A First Time for Everything' AS name, '9781250851048' AS code, '2 大人繪本與圖像小說' AS mid, '1204 圖像小說與大眾圖文' AS sub
    UNION ALL SELECT '1204260001' AS sku, 'Alyte' AS name, '9781990252471' AS code, '2 大人繪本與圖像小說' AS mid, '1204 圖像小說與大眾圖文' AS sub
    UNION ALL SELECT '1204260002' AS sku, '書店圖鑑：體驗一日店員，揭開書店工作日常！' AS name, '9789863779025' AS code, '2 大人繪本與圖像小說' AS mid, '1204 圖像小說與大眾圖文' AS sub
    UNION ALL SELECT '1205260007' AS sku, 'Cat Nap' AS name, '9780062671288' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260009' AS sku, 'Do You Remember?' AS name, '9780823442621' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260002' AS sku, 'Fireworks' AS name, '9780063216723' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260005' AS sku, 'I''m Like a Tree and a Tree''s Like Me' AS name, '9781592704248' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260012' AS sku, 'Mouse by the Sea: A Holiday in Nature' AS name, '9780500653654' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260013' AS sku, 'Mouse on the River' AS name, '9780500653289' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260008' AS sku, 'My Baba''s Garden' AS name, '9780823450831' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260010' AS sku, 'Shibu''s Tail' AS name, '9781646145737' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260011' AS sku, 'The Cat at Night' AS name, '9781782694618' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260006' AS sku, 'The Cat Who Couldn''t Be Bothered' AS name, '9780711287440' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260001' AS sku, 'The Tree and the River' AS name, '9781536223293' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260003' AS sku, 'The Weather Keeper' AS name, '9781592704958' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260004' AS sku, 'What a Small Cat Needs' AS name, '9781592704774' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260015' AS sku, 'ねこがしんぱい' AS name, '9784041158456' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260014' AS sku, 'ねこは るすばん' AS name, '9784593100965' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1205260016' AS sku, 'ひと粒のチョコレートに' AS name, '9784834087406' AS code, '2 大人繪本與圖像小說' AS mid, '1205 生活與心靈療癒主題' AS sub
    UNION ALL SELECT '1206260001' AS sku, 'Ahoy!' AS name, '9780593429396' AS code, '2 大人繪本與圖像小說' AS mid, '1206 文學、故事與無字書' AS sub
    UNION ALL SELECT '1206260010' AS sku, 'Dark Fairy Tales: Weird, Wicked Stories from Around the World' AS name, '9783791393506' AS code, '2 大人繪本與圖像小說' AS mid, '1206 文學、故事與無字書' AS sub
    UNION ALL SELECT '1206260006' AS sku, 'Odds and Ends' AS name, '9781592704798' AS code, '2 大人繪本與圖像小說' AS mid, '1206 文學、故事與無字書' AS sub
    UNION ALL SELECT '1206260003' AS sku, 'Sato the Rabbit, Morning Light' AS name, '9781592704392' AS code, '2 大人繪本與圖像小說' AS mid, '1206 文學、故事與無字書' AS sub
    UNION ALL SELECT '1206260005' AS sku, 'Snow White and Other Grimms'' Fairy Tales (MinaLima Ed.)' AS name, '9780063208247' AS code, '2 大人繪本與圖像小說' AS mid, '1206 文學、故事與無字書' AS sub
    UNION ALL SELECT '1206260008' AS sku, 'The Dragon Flower' AS name, '9781681379531' AS code, '2 大人繪本與圖像小說' AS mid, '1206 文學、故事與無字書' AS sub
    UNION ALL SELECT '1206260007' AS sku, 'The Eyes and the Impossible' AS name, '9781524764234' AS code, '2 大人繪本與圖像小說' AS mid, '1206 文學、故事與無字書' AS sub
    UNION ALL SELECT '1206260004' AS sku, 'The Little Mermaid and Other Fairy Tales: Lavishly Illustrated with In…' AS name, '9780062692597' AS code, '2 大人繪本與圖像小說' AS mid, '1206 文學、故事與無字書' AS sub
    UNION ALL SELECT '1206260009' AS sku, 'The Night' AS name, '9780735846289' AS code, '2 大人繪本與圖像小說' AS mid, '1206 文學、故事與無字書' AS sub
    UNION ALL SELECT '1206260011' AS sku, 'The Witch in the Tower' AS name, '9781529502923' AS code, '2 大人繪本與圖像小說' AS mid, '1206 文學、故事與無字書' AS sub
    UNION ALL SELECT '1206260002' AS sku, 'ここはおうち' AS name, '9784908356438' AS code, '2 大人繪本與圖像小說' AS mid, '1206 文學、故事與無字書' AS sub
    UNION ALL SELECT '1207260007' AS sku, 'Books and the People Who Make Them' AS name, '9783791375496' AS code, '2 大人繪本與圖像小說' AS mid, '1207 空間、書與閱讀主題' AS sub
    UNION ALL SELECT '1207260004' AS sku, 'Books Make Good Friends' AS name, '9781797209654' AS code, '2 大人繪本與圖像小說' AS mid, '1207 空間、書與閱讀主題' AS sub
    UNION ALL SELECT '1207260002' AS sku, 'Dear Bookstore' AS name, '9781536210699' AS code, '2 大人繪本與圖像小說' AS mid, '1207 空間、書與閱讀主題' AS sub
    UNION ALL SELECT '1207260008' AS sku, 'Franklin''s Flying Bookshop' AS name, '9780500651094' AS code, '2 大人繪本與圖像小說' AS mid, '1207 空間、書與閱讀主題' AS sub
    UNION ALL SELECT '1207260009' AS sku, 'Good Night, Little Bookshop' AS name, '9781529510027' AS code, '2 大人繪本與圖像小說' AS mid, '1207 空間、書與閱讀主題' AS sub
    UNION ALL SELECT '1207260001' AS sku, 'Here Is a Book' AS name, '9781419766756' AS code, '2 大人繪本與圖像小說' AS mid, '1207 空間、書與閱讀主題' AS sub
    UNION ALL SELECT '1207260005' AS sku, 'I Don''t Want to Read This Book' AS name, '9780593326060' AS code, '2 大人繪本與圖像小說' AS mid, '1207 空間、書與閱讀主題' AS sub
    UNION ALL SELECT '1207260011' AS sku, 'Meet the Lithographer' AS name, '9781644231104' AS code, '2 大人繪本與圖像小說' AS mid, '1207 空間、書與閱讀主題' AS sub
    UNION ALL SELECT '1207260003' AS sku, 'Nia and the New Free Library' AS name, '9781452166865' AS code, '2 大人繪本與圖像小說' AS mid, '1207 空間、書與閱讀主題' AS sub
    UNION ALL SELECT '1207260006' AS sku, 'The Bookshop Cat' AS name, '9781529041279' AS code, '2 大人繪本與圖像小說' AS mid, '1207 空間、書與閱讀主題' AS sub
    UNION ALL SELECT '1207260010' AS sku, 'This is a Story' AS name, '9781529516074' AS code, '2 大人繪本與圖像小說' AS mid, '1207 空間、書與閱讀主題' AS sub
    UNION ALL SELECT '1301260002' AS sku, 'Art Unpacked' AS name, '9780500025673' AS code, '3 藝術、設計與視覺' AS mid, '1301 世界美術史與理論批評' AS sub
    UNION ALL SELECT '1301260005' AS sku, 'E.H.Gombrich on Fresco Painting' AS name, '9780500027448' AS code, '3 藝術、設計與視覺' AS mid, '1301 世界美術史與理論批評' AS sub
    UNION ALL SELECT '1301260003' AS sku, 'James Hall on the Self-Portrait' AS name, '9780500027271' AS code, '3 藝術、設計與視覺' AS mid, '1301 世界美術史與理論批評' AS sub
    UNION ALL SELECT '1301260004' AS sku, 'Julian Bell on Painting' AS name, '9780500027288' AS code, '3 藝術、設計與視覺' AS mid, '1301 世界美術史與理論批評' AS sub
    UNION ALL SELECT '1301260006' AS sku, 'Linda Nochlin on the Body' AS name, '9780500027257' AS code, '3 藝術、設計與視覺' AS mid, '1301 世界美術史與理論批評' AS sub
    UNION ALL SELECT '1301260001' AS sku, 'The History of Art in One Sentence: 500 Years of Art But Funny' AS name, '9781526686893' AS code, '3 藝術、設計與視覺' AS mid, '1301 世界美術史與理論批評' AS sub
    UNION ALL SELECT '1301260007' AS sku, '藝術家的工作室：藝術產地x創作故事x行業祕聞的文化史' AS name, '9786267430156' AS code, '3 藝術、設計與視覺' AS mid, '1301 世界美術史與理論批評' AS sub
    UNION ALL SELECT '1302260054' AS sku, 'A Bigger Splash: Painting After Performance' AS name, '9781849760201' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260029' AS sku, 'An Artist A Day: A Daily Diary of Artists'' Birthdays' AS name, '9783791391694' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260052' AS sku, 'Art for All: Impressionists' AS name, '9783754401415' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260031' AS sku, 'Autumn in Japan: Woodblock Prints by Hokusai' AS name, '9783791393858' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260009' AS sku, 'Basquiat: The Modena Paintings' AS name, '9783775755092' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260050' AS sku, 'Botero' AS name, '9783836548823' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260032' AS sku, 'Cats of Japan: Woodblock Prints by Hokusai' AS name, '9783791377209' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260006' AS sku, 'Cherry Blossoms in Bloom: Great Works of Japanese' AS name, '9780847845637' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260012' AS sku, 'Dalí in 400 Images' AS name, '9789464781144' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260053' AS sku, 'Dalí: The Paintings' AS name, '9783836544924' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260048' AS sku, 'David Hockney: My Window' AS name, '9783836597968' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260007' AS sku, 'Flowers of Japan: Great Works of Japanese' AS name, '9780847876273' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260056' AS sku, 'Hiroshige''s One Hundred Famous Views of Edo' AS name, '9784805317716' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260057' AS sku, 'Hokusai Prints Sticker Book' AS name, '9780804858281' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260047' AS sku, 'Hokusai''s Method' AS name, '9780500028773' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260049' AS sku, 'HR Giger. (40th Ed.)' AS name, '9783836587020' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260035' AS sku, 'HR Giger: The Oeuvre Before Alien 1961-1976' AS name, '9783039421367' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260011' AS sku, 'Jean-Michel Basquiat: Engadin' AS name, '9783907493021' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260003' AS sku, 'Jean-Michel Basquiat: The Head-The Mind' AS name, '9780986000829' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260023' AS sku, 'Kahlo: The Bigger Picture' AS name, '9783791394398' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260043' AS sku, 'Katsushika Hokusai: 50 Masterpieces Explored' AS name, '9781923049369' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260028' AS sku, 'Klimt: The Bigger Picture' AS name, '9783791393995' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260026' AS sku, 'Matisse: The Bigger Picture' AS name, '9783791394015' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260046' AS sku, 'Matthew Wong: Vincent van Gogh' AS name, '9780500298053' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260055' AS sku, 'Meet the Artist: Piet Mondrian An Art Activity Book' AS name, '9781849768474' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260022' AS sku, 'Monet: The Bigger Picture' AS name, '9783791377827' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260017' AS sku, 'My Art Book of Adventure' AS name, '9781838666996' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260020' AS sku, 'My Art Book of Friendship' AS name, '9781838662592' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260018' AS sku, 'My Art Book of Happiness' AS name, '9781838660826' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260016' AS sku, 'My Art Book of Love' AS name, '9780714877181' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260019' AS sku, 'My Art Book of Sleep' AS name, '9780714878652' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260034' AS sku, 'My Museum' AS name, '9783791373195' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260004' AS sku, 'National Gallery of Art: Selected Works' AS name, '9780847839940' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260021' AS sku, 'Once More Unto the Breach: Samurai Warriors' AS name, '9784756252838' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260014' AS sku, 'Pablo Picasso: Boy Leading a Horse' AS name, '9781633451728' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260002' AS sku, 'Picasso: Printmaker' AS name, '9780714126999' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260008' AS sku, 'Raphael: Revolution in Tapestry Design' AS name, '9789464666595' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260013' AS sku, 'Salvador Dalí: The Persistence of Memory' AS name, '9781633451759' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260033' AS sku, 'Spring in Japan: Woodblock Prints by Hokusai' AS name, '9783791377971' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260025' AS sku, 'The Fantastic Gustave Doré' AS name, '9783791379630' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260005' AS sku, 'The Four Seasons: Great Works of Japanese Woodblock' AS name, '9780847845705' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260058' AS sku, 'The Louvre Art Deck: 52 Masterpieces' AS name, '9780789345516' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260030' AS sku, 'The Moon: Woodblock Prints by Hokusai' AS name, '9783791394619' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260039' AS sku, 'The Works: Claude Monet' AS name, '9781923503113' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260041' AS sku, 'The Works: Edgar Degas' AS name, '9781923503120' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260038' AS sku, 'The Works: Gustav Klimt' AS name, '9781923503045' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260042' AS sku, 'The Works: Katsushika Hokusai' AS name, '9781923239746' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260040' AS sku, 'The Works: Mary Cassatt' AS name, '9781923239722' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260037' AS sku, 'The Works: Vincent van Gogh' AS name, '9781923239739' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260045' AS sku, 'The World According to David Hockney' AS name, '9780500027042' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260044' AS sku, 'The World According to Vincent van Gogh' AS name, '9780500028728' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260051' AS sku, 'Turner (New Ed.)' AS name, '9783836504546' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260024' AS sku, 'Turner: Masters of Art' AS name, '9783791393421' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260027' AS sku, 'Van Gogh: The Bigger Picture' AS name, '9783791377599' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260059' AS sku, 'Yayoi Kusama: I Spend Each Day Embracing Flowers' AS name, '9781644231333' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260036' AS sku, 'Yayoi Kusama: Infinite Present' AS name, '9788857251424' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260015' AS sku, 'Yoshitomo Nara' AS name, '9780714879949' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260010' AS sku, 'Yoshitomo Nara' AS name, '9781853323829' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260001' AS sku, 'Yoshitomo Nara: My Imperfect Self' AS name, '9798998646409' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260063' AS sku, '中村萌 before the dawn' AS name, '9784568105674' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260061' AS sku, '飛行艇時代' AS name, '9784499228640' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260060' AS sku, '宮崎駿の雑想ノート' AS name, '9784499226776' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1302260062' AS sku, '新海誠監督作品 天気の子 公式ビジュアルガイド' AS name, '9784041084311' AS code, '3 藝術、設計與視覺' AS mid, '1302 藝術家作品集與館藏介紹' AS sub
    UNION ALL SELECT '1303260003' AS sku, 'Art & Soul: Paintings by Autumn Skye' AS name, '9780738779836' AS code, '3 藝術、設計與視覺' AS mid, '1303 平面設計與文字美學' AS sub
    UNION ALL SELECT '1303260002' AS sku, 'Designing Terry Pratchett''s Discworld' AS name, '9780063432420' AS code, '3 藝術、設計與視覺' AS mid, '1303 平面設計與文字美學' AS sub
    UNION ALL SELECT '1303260010' AS sku, 'Good Dogs: Canine Companions in Art and Illustration' AS name, '9789887684572' AS code, '3 藝術、設計與視覺' AS mid, '1303 平面設計與文字美學' AS sub
    UNION ALL SELECT '1303260005' AS sku, 'Make Every Day Creative: Art Anyone Can Do' AS name, '9781837760152' AS code, '3 藝術、設計與視覺' AS mid, '1303 平面設計與文字美學' AS sub
    UNION ALL SELECT '1303260007' AS sku, 'Picturebook Makers' AS name, '9781739979201' AS code, '3 藝術、設計與視覺' AS mid, '1303 平面設計與文字美學' AS sub
    UNION ALL SELECT '1303260006' AS sku, 'Picturebook Makers Part 2' AS name, '9781739979218' AS code, '3 藝術、設計與視覺' AS mid, '1303 平面設計與文字美學' AS sub
    UNION ALL SELECT '1303260001' AS sku, 'Stitching and Strolling' AS name, '9782330208004' AS code, '3 藝術、設計與視覺' AS mid, '1303 平面設計與文字美學' AS sub
    UNION ALL SELECT '1303260008' AS sku, 'Take a Line for a Walk' AS name, '9780500660393' AS code, '3 藝術、設計與視覺' AS mid, '1303 平面設計與文字美學' AS sub
    UNION ALL SELECT '1303260004' AS sku, 'Water Land: Land and Water Forms Around the World' AS name, '9781250152442' AS code, '3 藝術、設計與視覺' AS mid, '1303 平面設計與文字美學' AS sub
    UNION ALL SELECT '1303260011' AS sku, 'デザインのひきだし 53' AS name, '9784766139174' AS code, '3 藝術、設計與視覺' AS mid, '1303 平面設計與文字美學' AS sub
    UNION ALL SELECT '1303260012' AS sku, 'デザインのひきだし 56' AS name, '9784766140194' AS code, '3 藝術、設計與視覺' AS mid, '1303 平面設計與文字美學' AS sub
    UNION ALL SELECT '1303260013' AS sku, '做繪本的人' AS name, '9786264330008' AS code, '3 藝術、設計與視覺' AS mid, '1303 平面設計與文字美學' AS sub
    UNION ALL SELECT '1303260014' AS sku, 'Art Deco Playing Cards' AS name, '9781923239487' AS code, '3 藝術、設計與視覺' AS mid, '1303 平面設計與文字美學' AS sub
    UNION ALL SELECT '1303260015' AS sku, 'The Dictionary Story' AS name, '9781406395471' AS code, '3 藝術、設計與視覺' AS mid, '1303 平面設計與文字美學' AS sub
    UNION ALL SELECT '1304260004' AS sku, 'I Am. We Are. Liberty' AS name, '9781907112744' AS code, '3 藝術、設計與視覺' AS mid, '1304 設計專輯與品牌專刊' AS sub
    UNION ALL SELECT '1304260002' AS sku, 'Iro: The Essence of Colour in Japanese Design' AS name, '9781838664114' AS code, '3 藝術、設計與視覺' AS mid, '1304 設計專輯與品牌專刊' AS sub
    UNION ALL SELECT '1304260001' AS sku, 'Jean Jullien' AS name, '9781838663193' AS code, '3 藝術、設計與視覺' AS mid, '1304 設計專輯與品牌專刊' AS sub
    UNION ALL SELECT '1304260003' AS sku, 'nendo: A Big Book of Small Ideas' AS name, '9780500026489' AS code, '3 藝術、設計與視覺' AS mid, '1304 設計專輯與品牌專刊' AS sub
    UNION ALL SELECT '1304260005' AS sku, 'SOU・SOU 20年のテキスタイルデザイン集' AS name, '9784568105575' AS code, '3 藝術、設計與視覺' AS mid, '1304 設計專輯與品牌專刊' AS sub
    UNION ALL SELECT '1305260007' AS sku, 'I Know Kung Fu' AS name, '9789887462903' AS code, '3 藝術、設計與視覺' AS mid, '1305 技法、材料與收藏愛好' AS sub
    UNION ALL SELECT '1305260006' AS sku, 'Sketched Out: Artistic Sketchbooks and Journals' AS name, '9789887684480' AS code, '3 藝術、設計與視覺' AS mid, '1305 技法、材料與收藏愛好' AS sub
    UNION ALL SELECT '1305260003' AS sku, 'Sticker Studio: Dark Academia' AS name, '9781250457417' AS code, '3 藝術、設計與視覺' AS mid, '1305 技法、材料與收藏愛好' AS sub
    UNION ALL SELECT '1305260002' AS sku, 'Sticker Studio: Flora' AS name, '9781250279477' AS code, '3 藝術、設計與視覺' AS mid, '1305 技法、材料與收藏愛好' AS sub
    UNION ALL SELECT '1305260004' AS sku, 'The Curious Collections Sticker Book' AS name, '9781837760916' AS code, '3 藝術、設計與視覺' AS mid, '1305 技法、材料與收藏愛好' AS sub
    UNION ALL SELECT '1305260001' AS sku, 'The Dot (Deluxe Gift Ed.)' AS name, '9781536255171' AS code, '3 藝術、設計與視覺' AS mid, '1305 技法、材料與收藏愛好' AS sub
    UNION ALL SELECT '1305260005' AS sku, 'The Sticker Treasury of Woodland Adventures' AS name, '9781923239388' AS code, '3 藝術、設計與視覺' AS mid, '1305 技法、材料與收藏愛好' AS sub
    UNION ALL SELECT '1305260009' AS sku, '一葉一世界：日本藝術家的89個迷你葉雕童話' AS name, '9789864896035' AS code, '3 藝術、設計與視覺' AS mid, '1305 技法、材料與收藏愛好' AS sub
    UNION ALL SELECT '1305260008' AS sku, '手作的人：二十處扎根土地的手作工坊' AS name, '9789865437558' AS code, '3 藝術、設計與視覺' AS mid, '1305 技法、材料與收藏愛好' AS sub
    UNION ALL SELECT '1401260009' AS sku, '二手書店店員告白' AS name, '9789570866902' AS code, '4 綜合人文與故事' AS mid, '1401 有關書店的故事與閱讀' AS sub
    UNION ALL SELECT '1401260005' AS sku, '世界盡頭的二間袖珍書店' AS name, '9786264199056' AS code, '4 綜合人文與故事' AS mid, '1401 有關書店的故事與閱讀' AS sub
    UNION ALL SELECT '1401260006' AS sku, '在世界盡頭的書店' AS name, '9786263962866' AS code, '4 綜合人文與故事' AS mid, '1401 有關書店的故事與閱讀' AS sub
    UNION ALL SELECT '1401260001' AS sku, '那些書店這些人：書店如何形塑文化與生活，獻給書店與書店員的情書' AS name, '9786264331081' AS code, '4 綜合人文與故事' AS mid, '1401 有關書店的故事與閱讀' AS sub
    UNION ALL SELECT '1401260007' AS sku, '書店有時' AS name, '9789887967071' AS code, '4 綜合人文與故事' AS mid, '1401 有關書店的故事與閱讀' AS sub
    UNION ALL SELECT '1401260008' AS sku, '停下來的書店' AS name, '9789869966122' AS code, '4 綜合人文與故事' AS mid, '1401 有關書店的故事與閱讀' AS sub
    UNION ALL SELECT '1401260004' AS sku, '從前從前，有間古書店：英倫傳奇書店亨利・莎樂倫的日常' AS name, '4710405007608' AS code, '4 綜合人文與故事' AS mid, '1401 有關書店的故事與閱讀' AS sub
    UNION ALL SELECT '1401260002' AS sku, '情熱書店：史上最偏心！書店店員的東京獨立書店一手訪談' AS name, '9789869726306' AS code, '4 綜合人文與故事' AS mid, '1401 有關書店的故事與閱讀' AS sub
    UNION ALL SELECT '1401260003' AS sku, '歡迎光臨小林書店' AS name, '9786267637593' AS code, '4 綜合人文與故事' AS mid, '1401 有關書店的故事與閱讀' AS sub
    UNION ALL SELECT '1402260005' AS sku, 'Alice''s Adventures in Wonderland & Through the Looking-Glass' AS name, '9780062936615' AS code, '4 綜合人文與故事' AS mid, '1402 國內外文學與小說' AS sub
    UNION ALL SELECT '1402260007' AS sku, 'Frankenstein (Minalima Edition)' AS name, '9780063452060' AS code, '4 綜合人文與故事' AS mid, '1402 國內外文學與小說' AS sub
    UNION ALL SELECT '1402260004' AS sku, 'Peter Pan' AS name, '9780062362223' AS code, '4 綜合人文與故事' AS mid, '1402 國內外文學與小說' AS sub
    UNION ALL SELECT '1402260006' AS sku, 'The Adventures of Pinocchio' AS name, '9780062905277' AS code, '4 綜合人文與故事' AS mid, '1402 國內外文學與小說' AS sub
    UNION ALL SELECT '1402260003' AS sku, 'The Jungle Book' AS name, '9780062389503' AS code, '4 綜合人文與故事' AS mid, '1402 國內外文學與小說' AS sub
    UNION ALL SELECT '1402260002' AS sku, 'The Secret Garden (Illustrated Ed.)' AS name, '9780062692573' AS code, '4 綜合人文與故事' AS mid, '1402 國內外文學與小說' AS sub
    UNION ALL SELECT '1402260001' AS sku, 'The Wonderful Wizard of Oz' AS name, '9780063055735' AS code, '4 綜合人文與故事' AS mid, '1402 國內外文學與小說' AS sub
    UNION ALL SELECT '1403260004' AS sku, 'Nature Notes of an Edwardian Lady' AS name, '9780789344281' AS code, '4 綜合人文與故事' AS mid, '1403 科普、地理與人文社科' AS sub
    UNION ALL SELECT '1403260003' AS sku, 'Neko: Cats of Japan' AS name, '9781923239760' AS code, '4 綜合人文與故事' AS mid, '1403 科普、地理與人文社科' AS sub
    UNION ALL SELECT '1403260002' AS sku, 'The Country Diary of an Edwardian Lady' AS name, '9780847858903' AS code, '4 綜合人文與故事' AS mid, '1403 科普、地理與人文社科' AS sub
    UNION ALL SELECT '1403260001' AS sku, 'The Curator''s Collection: An A to Z of Plants' AS name, '9781835879214' AS code, '4 綜合人文與故事' AS mid, '1403 科普、地理與人文社科' AS sub
    UNION ALL SELECT '1403260005' AS sku, 'This Is Paris' AS name, '9780789346339' AS code, '4 綜合人文與故事' AS mid, '1403 科普、地理與人文社科' AS sub
    UNION ALL SELECT '1403260007' AS sku, '和煦的一天' AS name, '9789865323271' AS code, '4 綜合人文與故事' AS mid, '1403 科普、地理與人文社科' AS sub
    UNION ALL SELECT '1403260006' AS sku, '美しいアンティーク鉱物画の本（増補愛蔵版）' AS name, '9784422440408' AS code, '4 綜合人文與故事' AS mid, '1403 科普、地理與人文社科' AS sub
    UNION ALL SELECT '1404260001' AS sku, 'Art: An Interactive Guide' AS name, '9781638190165' AS code, '4 綜合人文與故事' AS mid, '1404 哲學、成長與藝術教育' AS sub
    UNION ALL SELECT '1404260006' AS sku, 'Begin Again, and Again, and Again: Notes on the Art of Perpetual R…' AS name, '9781523531714' AS code, '4 綜合人文與故事' AS mid, '1404 哲學、成長與藝術教育' AS sub
    UNION ALL SELECT '1404260003' AS sku, 'I Used to Have a Plan: But Life Had Other Ideas' AS name, '9780062973627' AS code, '4 綜合人文與故事' AS mid, '1404 哲學、成長與藝術教育' AS sub
    UNION ALL SELECT '1404260004' AS sku, 'My Art Book of Peace' AS name, '9781837291526' AS code, '4 綜合人文與故事' AS mid, '1404 哲學、成長與藝術教育' AS sub
    UNION ALL SELECT '1404260005' AS sku, 'The Big Book of Nature Art' AS name, '9780500652930' AS code, '4 綜合人文與故事' AS mid, '1404 哲學、成長與藝術教育' AS sub
    UNION ALL SELECT '1404260002' AS sku, 'This Is Orange: A Field Trip Through Color' AS name, '9781536230529' AS code, '4 綜合人文與故事' AS mid, '1404 哲學、成長與藝術教育' AS sub
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
