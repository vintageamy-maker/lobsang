#!/bin/bash
# ─────────────────────────────────────────────────────────────
# 部署一個站的 API＋前端，並用 **md5 比對** 確認真的裝到指定的那一包。
#
# ⚠⚠ 為什麼需要這支（2026-09-13 實際出事）：
#   部署腳本裡的檔名是寫死的（`/tmp/st-api.tgz`），
#   我把新版打包成 `fix-api.tgz` 上傳，腳本照樣解開 VM 上**殘留的舊 st-api.tgz**
#   ⇒ 正式站的後端**悄悄退版**，而部署輸出從頭到尾看起來都正常
#     （容器有停有起、前端一致性檢查通過、API 回 401、例外 0）。
#   後果：DbSeeder 的 Production 守門跟著退掉，測試帳號第三次被種回來。
#
#   ⭐ 根因不只是打錯字 —— VM 的 /tmp 堆了 **80 幾個歷史打包檔**，
#      檔名一重用就會裝到舊的，而且不會有任何錯誤。
#
# ⇒ 判準：**部署後要驗「裝到的是不是我送的那一份」，不能只驗「有沒有啟動」。**
#   啟動成功只證明那份 dll 能跑，不證明它是新的。
#
# 用法：
#   bash deploy-verify.sh <站台目錄> <容器名> <api.tgz> [front.tgz]
# 例：
#   bash deploy-verify.sh ~/lobsang lobsang-api /tmp/api-20261015-1.tgz /tmp/admin-20261015-1.tgz
#
# ⭐ 建議打包檔名一律帶日期時間，不要重用 st-api.tgz 這種固定名字。
# ─────────────────────────────────────────────────────────────
set -uo pipefail

D=${1:?站台目錄}
CONTAINER=${2:?容器名}
API_TGZ=${3:?api.tgz 路徑}
FRONT_TGZ=${4:-}
# ⭐ dll 名稱＝專案名稱（新專案複製 Base 後改名，這裡用環境變數帶，不必改腳本）
DLL=${DLL:-Lobsang.dll}

TS=$(date +%Y%m%d-%H%M%S)
W=$(mktemp -d)
trap 'rm -rf "$W"' EXIT

[ -f "$API_TGZ" ] || { echo "⚠ 找不到 $API_TGZ"; exit 1; }

echo "########## 0. 先算出「要裝的」dll 雜湊 ##########"
mkdir -p "$W/api"
tar xzf "$API_TGZ" -C "$W/api" || exit 1
NEW_DLL="$W/api/publish/${DLL}"
[ -f "$NEW_DLL" ] || { echo "⚠ 包裡沒有 publish/${DLL}（專案改名了嗎？用 DLL=xxx.dll 指定）"; exit 1; }
WANT=$(md5sum "$NEW_DLL" | cut -d' ' -f1)
NOW=$(md5sum "$D/publish/${DLL}" 2>/dev/null | cut -d' ' -f1 || echo "（無）")
echo "來源檔 : $API_TGZ（$(date -r "$API_TGZ" '+%m-%d %H:%M')）"
echo "要裝的 : $WANT"
echo "現在跑 : $NOW"
if [ "$WANT" = "$NOW" ]; then
  echo "⇒ 已經是這個版本。仍會照常重裝（確保 Templates 等附檔也同步）。"
fi

echo
echo "########## 1. 備份現行版本 ##########"
cp "$D/publish/${DLL}" "$D/${DLL}.bak.$TS" 2>/dev/null && echo "dll → ${DLL}.bak.$TS"
[ -n "$FRONT_TGZ" ] && cp -r "$D/admin-dist" "$D/admin-dist.bak.$TS" 2>/dev/null && echo "前端 → admin-dist.bak.$TS"

echo
echo "########## 2. 停容器 → cp 覆蓋 → 啟動 ##########"
# ⚠ 一律 cp 覆蓋，不 mv、不先刪 —— bind mount 綁 inode，換掉 inode 容器就看不到新檔
sudo docker stop "$CONTAINER"
sudo cp -r "$W/api/publish/." "$D/publish/"
sudo docker start "$CONTAINER"

echo
echo "########## 3. ⭐⭐ 驗證裝到的版本（這一步是重點）##########"
AFTER=$(md5sum "$D/publish/${DLL}" | cut -d' ' -f1)
echo "換完後 : $AFTER"
if [ "$AFTER" != "$WANT" ]; then
  echo "⚠⚠ 雜湊不一致 —— 裝到的不是我送的那一份。中止。"
  exit 1
fi
echo "✅ 雜湊一致"

if [ -n "$FRONT_TGZ" ] && [ -f "$FRONT_TGZ" ]; then
  echo
  echo "########## 4. 換前端 ##########"
  mkdir -p "$W/front"
  tar xzf "$FRONT_TGZ" -C "$W/front" || exit 1
  [ -f "$W/front/index.html" ] || { echo "⚠ 包裡沒有 index.html"; exit 1; }
  # ⚠ 只清內容，絕不 rm -rf 資料夾再重建（同樣是 inode 問題，而且 nginx 不會報錯）
  sudo rm -rf "$D/admin-dist"/*
  sudo cp -r "$W/front/." "$D/admin-dist/"
  # ⚠ 在 Windows 打的 tar 會把權限存成 700/600 ⇒ nginx 讀不到、整站 403（Base 建站 2026-10-15 踩到）
  sudo chmod -R a+rX "$D/admin-dist"
  echo "-- index.html 指到的檔在不在 --"
  (cd "$D/admin-dist" && grep -oE '/admin/assets/[^"]+' index.html | while read -r p; do
      f=".${p#/admin}"; [ -f "$f" ] && echo "OK   $p" || { echo "⚠ 缺  $p"; exit 1; }
    done)
fi

echo
echo "########## 5. 等 API 起來 ##########"
IP=$(sudo docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$CONTAINER")
UP=no
for i in $(seq 1 60); do
  C=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "http://$IP:8080/api/admin/users")
  [ "$C" = "401" ] && { echo "第 $i 次起來了（401＝路由在且受保護）"; UP=yes; break; }
  sleep 3
done
[ "$UP" = yes ] || { echo "⚠⚠ API 沒起來"; exit 1; }

echo "環境 : $(sudo docker logs --since 5m "$CONTAINER" 2>&1 | grep -i 'Hosting environment' | tail -1)"
# ⚠ 不可只 grep exception：開發站是 Debug 等級會印 SQL，error_logs 的欄位 exception_type 會被算進去（假警報）
echo "例外 : $(sudo docker logs --since 5m "$CONTAINER" 2>&1 | grep -cE '^fail: |[Uu]nhandled exception')"
echo
echo "✅ 部署完成。⚠ 別忘了：**登入後的畫面要由人工確認**，本腳本驗不到。"
