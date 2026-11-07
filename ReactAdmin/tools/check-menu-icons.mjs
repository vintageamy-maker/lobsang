/**
 * 檢查「選單種子用到的圖示 key」是不是都註冊在 ICON_MAP 裡。
 *
 * ── 為什麼需要這支 ─────────────────────────────────────────────
 * ⚠⚠ 對不到 key 是**靜默失效**：iconOf() 只在瀏覽器 console 印一次警告，
 *    然後回 undefined。畫面不會壞、不會有紅字，只是那一項沒有圖示 ——
 *    從畫面上看不出原因，很容易被當成「那頁忘了設圖示」，
 *    但資料庫裡其實有值，錯的是前端沒註冊。
 *
 * 執行：node tools/check-menu-icons.mjs
 * ⚠ 這是**純文字比對**，不連資料庫 —— 比對的是 DbSeeder.cs 裡寫死的 Icon = "xxx"。
 *   業主日後在「選單管理」自行改過的圖示不在檢查範圍內。
 */
import { readFileSync, existsSync } from 'node:fs'
import { resolve } from 'node:path'

const SEEDER = resolve('../API/Lobsang/Data/DbSeeder.cs')
const ICONS = resolve('src/icons.tsx')

let seeder, icons
if (!existsSync(SEEDER)) {
  console.log('  （找不到 ' + SEEDER + '，略過選單圖示檢查）')
  process.exit(0)
}
try {
  seeder = readFileSync(SEEDER, 'utf8')
  icons = readFileSync(ICONS, 'utf8')
} catch (e) {
  console.error(`  ⚠ 讀不到檔案：${e.message}`)
  process.exit(1)
}

/** 種子裡所有 Icon = "xxx" 的值 */
const used = new Set([
  // 物件初始化式：Icon = "xxx"
  ...[...seeder.matchAll(/Icon\s*=\s*"([a-z0-9-]+)"/g)].map(m => m[1]),
  // EnsureMenuDirAsync(db, "名稱", "icon", 排序)
  ...[...seeder.matchAll(/EnsureMenuDirAsync\(\s*\w+\s*,\s*"[^"]*"\s*,\s*"([a-z0-9-]+)"/g)].map(m => m[1]),
  // EnsureMenuAsync(db, 上層, "名稱", "/路徑", "icon", 排序, 權限)
  ...[...seeder.matchAll(/EnsureMenuAsync\(\s*\w+\s*,\s*\w+\s*,\s*"[^"]*"\s*,\s*"[^"]*"\s*,\s*"([a-z0-9-]+)"/g)].map(m => m[1]),
])

/** ICON_MAP 裡註冊的 key */
const registered = new Set(
  [...icons.matchAll(/^\s*'([a-z0-9-]+)':\s*</gm)].map(m => m[1]))

const missing = [...used].filter(k => !registered.has(k)).sort()
const unused = [...registered].filter(k => !used.has(k)).sort()

console.log(`  種子用到 ${used.size} 種圖示；ICON_MAP 註冊 ${registered.size} 種`)

if (unused.length) {
  // ⚠ 這一項只是提示不是錯誤 —— 多註冊幾個備用的 key 沒有壞處，
  //   而且「選單管理」的快選清單會用到整張表。
  console.log(`  （備用未使用：${unused.join('、')}）`)
}

if (missing.length === 0) {
  console.log('  ✅ 沒有對不到的圖示 key')
  process.exit(0)
}

console.error(`  ⚠⚠ 有 ${missing.length} 個 key 種子用了、ICON_MAP 沒註冊：${missing.join('、')}`)
console.error('     那幾個選單會**沒有圖示**，而且不會有任何錯誤訊息。')
console.error(`     請到 ${ICONS} 補上。`)
process.exit(1)
