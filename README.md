# Lobsang

> 合滙科技。2026-11-07 從 Base（基礎後台＋API 範本，commit d7a4f96）複製出來。共用模組的說明看 Base 的知識庫 `base/`；本專案的筆記在 `lobsang/`。

| 資料夾 | 內容 |
|---|---|
| `API/Lobsang/` | .NET 8 Web API ＋ EF Core（MySQL） |
| `ReactAdmin/` | 後台（React＋Vite＋antd），網址 `/admin/` |
| `React/` | 主站：公開查書頁（不用登入） |
| `deploy/local/` | 本機 MySQL（docker compose） |
| `deploy/stage/` | 開發站設定（從 Base 改名而來，**尚未部署**，網址暫定 lobsang.helwin.com.tw） |
| `lobsang/` | 知識庫 |

## 本機開發

```bash
# 1. 資料庫（第一次要先把 deploy/local/env.example 複製成 .env 填密碼）
cd deploy/local && docker compose up -d
# 2. API（需要 API/Lobsang/appsettings.Development.json，不入版控）→ http://localhost:5080
cd API/Lobsang && dotnet run --launch-profile http
# 3. 後台 → http://localhost:5173/admin/
cd ReactAdmin && npm ci && npm run dev
# 4. 主站（公開查書頁）→ http://localhost:5174/
cd React && npm ci && npm run dev -- --port 5174 --strictPort
# ⚠ 型別檢查用 npx tsc -b（不是 tsc --noEmit）
```
