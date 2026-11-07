namespace Lobsang.Auth
{
    /// <summary>
    /// LINE 專用權杖（<c>src=line</c>）能做什麼。**兩張白名單，互相獨立，不是 else if。**
    ///
    /// ⚠⚠ 為什麼光靠權限碼不夠 —— 這是本檔存在的全部理由：
    /// <code>
    ///   WorkRecordsController.Approve / Reject  →  [RequirePermission("record.read")]
    /// </code>
    /// **「填單／看單」與「簽核」掛的常常是同一個權限碼。** 也就是說，只要那顆權杖帶得動
    /// <c>record.read</c>，它就呼叫得了 <c>approve</c>，而且會順利通過
    /// <c>ApprovalResolver.CanAct</c>（那位主管本來就是這關的簽核人）。
    ///
    /// ⇒ 原則「LINE 不做簽核」。若只靠<b>前端不放按鈕</b>，那句話就只是
    /// 「沒有按鈕」，不是事實 —— 任何人用瀏覽器開發者工具就能繞過。
    /// **能力的邊界一定要畫在 API 這一側。**
    /// （簽核要看完整內容、附件與前後關卡，手機上的短命權杖不適合做這種決定。）
    ///
    /// ⭐ 用**允許清單**不用黑名單：日後新增的端點預設是「不給 LINE 用」，
    /// 而不是「忘了加進黑名單就默默開放」。失敗方向要倒向少一個功能，不是多一個洞。
    /// </summary>
    public static class LineSessionPolicy
    {
        /// <summary>權杖上可以帶的權限碼。使用者實際擁有的權限會與這張表取交集。</summary>
        /// <remarks>
        /// ⭐ 範本只開「表單引擎的填寫／檢視」。**新專案要讓 LIFF 頁面用自己的功能時，
        ///   在這裡加上那個功能的「本人操作」權限碼**，並同時在下方 <see cref="AllowedRoutes"/> 開路由 ——
        ///   只加一邊的話，症狀是 LIFF 上看到 403「沒有開放給 LINE 使用」或「權限不足」。
        /// ⚠⚠ <b>絕對不可以放 <c>system.admin</c></b> ——
        /// <see cref="RequirePermissionAttribute"/> 對它一律放行，
        /// 放進來這顆權杖就是全系統通行證。
        /// ⚠ 也不放任何 <c>*.manage</c>、<c>*.cancel</c>（例如 <c>record.cancel</c>）：
        /// 代他人操作、作廢已核准的單在 LINE 上刻意做不到，
        /// 而且是**結構上做不到**，不是靠前端不顯示。
        /// ⚠ 新增時優先用「只能動自己那一份」的權限碼（例如 <c>xxx.self</c>），
        /// 不要直接放後台的管理權限 —— 那等於讓每一支綁定的手機都能查全體資料。
        /// </remarks>
        public static readonly HashSet<string> AllowedPermissions = new(StringComparer.Ordinal)
        {
            /*
             * ⚠⚠ `record.read` 少不得，而且少了的症狀非常難查。
             *
             * `FilesController.List` 撈完之後**還會再依每個檔案的分類讀取權過濾一次**
             * （FileAccessPolicy.CanRead）。只給寫入權的話 ⇒ **上傳回 200、列表回 `[]`**。
             * 使用者在 LIFF 上看到的是「傳完了，但畫面說沒有檔案」，
             * 於是他會一直重傳 —— 而每一次其實都成功了。「看起來有效 ≠ 真的有效」。
             *
             * ⚠ 簽核不會因此被打開：能打哪些端點**仍由路由白名單決定**，
             *   approve／reject 不在白名單上，照樣打不到。兩道鎖各管各的。
             */
            "record.read",
            "record.write",   // 表單填寫、送出、刪自己的草稿、上傳附件（FileCategory.Record 的寫入權）
        };

        /// <summary>
        /// 允許呼叫的路由。比對方式是「HTTP 動詞 ＋ 路由樣板」，
        /// 用樣板（含 <c>{id}</c>）而不是實際網址，才不會被 id 的值影響。
        /// </summary>
        /// <remarks>
        /// ⚠ 刻意<b>不在</b>清單上的（逐條寫出來，免得日後有人以為是漏掉的）：
        /// <list type="bullet">
        ///   <item><c>approve</c> / <c>reject</c> / <c>approve-batch</c> —— LINE 不做簽核</item>
        ///   <item><c>pending-approvals</c> —— 那是簽核者的清單</item>
        ///   <item><c>cancel</c>（作廢）—— 要 <c>record.cancel</c>，後台的事</item>
        ///   <item><c>withdraw</c>（抽單）—— 少一條路少一組狀態；要抽單請用後台</item>
        ///   <item><c>GET api/files/{id}</c>（下載）、<c>DELETE api/files/{id}</c> ——
        ///     附件可能含個資，下載要留調閱紀錄，在手機上看也容易被截圖外流，留在後台做</item>
        /// </list>
        /// ⭐ 新專案加 LIFF 功能時，在這裡依同樣格式加「列出 → 新增 → 送出 → 刪自己的草稿」那幾條就好。
        /// </remarks>
        public static readonly HashSet<string> AllowedRoutes = new(StringComparer.OrdinalIgnoreCase)
        {
            // ── 表單引擎：表單定義（填單前要知道欄位）──
            "GET api/admin/form-definitions",
            "GET api/admin/form-definitions/{id}",

            // ── 表單引擎：工作記錄（列出 → 新增 → 修改草稿 → 送出 → 刪草稿）──
            // ⚠ 列表看得到什麼由 WorkRecordsController 的可見範圍決定（record.read 的範圍），
            //   LINE 權杖**沒有另外縮小**。若新專案的記錄含敏感內容，要在控制器裡對 src=line 只回本人。
            "GET api/admin/work-records",
            "GET api/admin/work-records/{id}",
            "POST api/admin/work-records",
            "PUT api/admin/work-records/{id}",
            "POST api/admin/work-records/{id}/submit",
            "DELETE api/admin/work-records/{id}",

            // ── 附件：只開上傳與列出 ──
            "POST api/files",
            "GET api/files",
        };

        /// <summary>
        /// 把這一次請求的動詞與路由樣板組成比對用的字串。
        ///
        /// ⚠⚠ 路由樣板**帶著型別限制**：實際拿到的是
        /// <c>api/admin/work-records/{id:int}/submit</c>，不是 <c>{id}</c>。
        /// 第一版白名單寫成 <c>{id}</c>，於是**每一條帶 id 的規則都對不上**。
        /// 當下看起來完全正常（因為不在清單就拒絕，而測的剛好都是該拒絕的），
        /// 要等到正常的送出被誤擋才會發現 —— 又一次「看起來有效 ≠ 真的有效」。
        ///
        /// ⇒ 比對前先把 <c>:限制</c>、<c>?</c>、<c>=預設值</c> 都去掉，只留參數名。
        ///   這樣日後有人把 <c>{id:int}</c> 改成 <c>{id:int:min(1)}</c> 也不會靜靜壞掉。
        /// </summary>
        public static string Key(string method, string? routeTemplate)
        {
            var t = routeTemplate?.TrimStart('/') ?? "";
            // {id:int} / {id?} / {page=1} → {id} / {id} / {page}
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\{([A-Za-z_][A-Za-z0-9_]*)[^}]*\}", "{$1}");
            return $"{method} {t}";
        }
    }
}
