const TOKEN_KEY = 'lobsang_admin_token'
const USER_KEY = 'lobsang_admin_user'

/** 可見範圍：只有自己／所轄單位（含子單位）／全機構 */
export type DataScope = 'Self' | 'Unit' | 'All'

/** 各模組的可見範圍（模組代碼 → 範圍），由後端判定後隨登入回應帶下來；沒有的模組一律當成 Self */
export type UserScopes = Record<string, DataScope>

export interface LoginUser {
  id: number
  name: string
  account: string
  institutionId: number
  roles: string[]
  permissions: string[]
  /** 後端 /login 與 /me 早已回傳這三個欄位，只是型別沒宣告 */
  orgUnitId?: number | null
  orgUnitName?: string | null
  title?: string | null
  /** 舊權杖存下來的 user 沒有這個欄位，取用時一律當成 Self */
  scopes?: UserScopes
  /**
   * ⚠ 後端 /login 與 /me 從一開始就回傳這個欄位，但前端一直沒接（2026-08-22 補）。
   *
   * ⚠⚠ 沒接的後果不是「少一個提示」：後端在使用者被設成強制換密碼時會回 true，
   *    而前端既不理它、也**沒有任何修改密碼的畫面** ——
   *    那個機制等於攔不住任何人，而且被設到的人也沒有出路。
   */
  mustChangePassword?: boolean
}

export const auth = {
  token: () => localStorage.getItem(TOKEN_KEY),
  user: (): LoginUser | null => {
    const raw = localStorage.getItem(USER_KEY)
    return raw ? (JSON.parse(raw) as LoginUser) : null
  },
  save: (token: string, user: LoginUser) => {
    localStorage.setItem(TOKEN_KEY, token)
    localStorage.setItem(USER_KEY, JSON.stringify(user))
  },
  clear: () => {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(USER_KEY)
    // ⚠ 認證密碼的解鎖憑證存在 **sessionStorage**，清 localStorage 清不到它。
    //    少了這一行，登出後憑證還留在分頁裡，重新登入就直接跳過第二道門。
    //    （後端另有「登入即作廢」把關 —— 這裡清掉是為了不讓過期憑證繼續被送出去。）
    sessionStorage.removeItem(STEP_UP_KEY)
  },
  hasPerm: (code: string) => {
    const u = auth.user()
    return !!u && (u.permissions.includes(code) || u.permissions.includes('system.admin'))
  },
  /**
   * 此人在某模組的可見範圍。
   *
   * ⚠ **不要用 `hasPerm('xxx.manage')` 代替這個。** `xxx.manage` 是「看得到全機構」，
   * 而單位主管一個 manage 權限都沒有，卻看得到所轄單位 —— 用權限碼判斷會把單位主管
   * 判成只看得到自己。
   * 「能做什麼」用 hasPerm，「看得到誰」用這一支。
   */
  scopeOf: (module: string): DataScope =>
    auth.user()?.scopes?.[module] ?? 'Self',
  /** 可見範圍是否及於他人（＝畫面上該不該出現範圍切換與代理對象選單） */
  canSeeOthers: (module: string) => auth.scopeOf(module) !== 'Self',
}

export class ApiError extends Error {
  status: number
  code?: string
  constructor(status: number, message: string, code?: string) {
    super(message)
    this.status = status
    this.code = code
  }
}

/* ══════════ 認證密碼（二次密碼） ══════════
 * ⭐ 這道鎖真正的門在後端；這裡只負責把憑證帶上去、以及在後端說「要認證」時通知畫面。
 *    ⚠ 不可用前端旗標判斷「已認證」—— 那種旗標改一下就過了。
 */
const STEP_UP_HEADER = 'X-StepUp-Token'
const STEP_UP_KEY = 'sc_stepup'

/** ⚠ 存 sessionStorage 不是 localStorage —— 關掉分頁就該重新認證 */
export const stepUpToken = () => sessionStorage.getItem(STEP_UP_KEY)
export const setStepUpToken = (t: string | null) =>
  t ? sessionStorage.setItem(STEP_UP_KEY, t) : sessionStorage.removeItem(STEP_UP_KEY)

const stepUpExpiryListeners = new Set<(seconds: number) => void>()
export const onStepUpExpiry = (fn: (seconds: number) => void) => {
  stepUpExpiryListeners.add(fn)
  return () => { stepUpExpiryListeners.delete(fn) }
}

/** 後端回 403 step_up_required 時通知畫面跳出認證視窗 */
const stepUpRequiredListeners = new Set<(code: string) => void>()
export const onStepUpRequired = (fn: (code: string) => void) => {
  stepUpRequiredListeners.add(fn)
  return () => { stepUpRequiredListeners.delete(fn) }
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers: Record<string, string> = { ...(options.headers as Record<string, string>) }
  // ⚠ FormData 不可自行設 Content-Type —— multipart 的 boundary 必須由瀏覽器產生，
  //    手動蓋成 application/json 會讓後端整包解析失敗
  if (options.body && !(options.body instanceof FormData))
    headers['Content-Type'] = 'application/json'
  const token = auth.token()
  if (token) headers['Authorization'] = `Bearer ${token}`
  // 認證密碼（二次密碼）的解鎖憑證。⭐ 一次認證、所有受保護的頁共用，
  // 所以放在共用的 request 而不是各頁自己帶。
  const stepUp = stepUpToken()
  if (stepUp) headers[STEP_UP_HEADER] = stepUp

  const res = await fetch(path, { ...options, headers })

  // 後端每次都回剩餘時間 —— ⚠ 倒數以**後端**為準，前端的計時器只是顯示
  const exp = res.headers.get('X-StepUp-Expires-In')
  if (exp) stepUpExpiryListeners.forEach(fn => fn(Number(exp)))

  if (res.status === 401) {
    auth.clear()
    window.location.href = '/admin/login'
    throw new ApiError(401, '登入已過期，請重新登入')
  }

  if (!res.ok) {
    const body = await res.json().catch(() => null)
    // 需要認證密碼：通知畫面跳出認證視窗，並清掉已失效的憑證
    if (res.status === 403 &&
        (body?.code === 'step_up_required' || body?.code === 'step_up_not_set')) {
      setStepUpToken(null)
      stepUpRequiredListeners.forEach(fn => fn(body.code))
    }
    throw new ApiError(res.status, body?.message ?? `請求失敗（${res.status}）`, body?.code)
  }

  // ⚠ 後端有不少端點回 `Ok()` —— HTTP 200 但**沒有 body**。
  //    直接 res.json() 會丟 SyntaxError，而那不是 ApiError，
  //    頁面上只會看到「操作失敗」，實際上後端已經做完了 ——
  //    使用者會重按一次，或以為沒生效。空 body 一律當成 undefined。
  if (res.status === 204) return undefined as T
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'PUT', body: body === undefined ? undefined : JSON.stringify(body) }),
  del: <T>(path: string) => request<T>(path, { method: 'DELETE' }),

  /**
   * POST 後取回檔案（匯出用）。
   *
   * ⚠ 與 blob() 分開而不是加參數：匯出要送一整包「選定值」，
   * 而且後端會在標頭回**警告訊息**（例如某個選項在範本上找不到）——
   * 那些一定要讓使用者看見，不能默默吞掉。
   */
  blobPost: (path: string, body?: unknown) => blobRequest(path, "POST", body),
  /** GET 取回檔案 —— 沒有「選定值」要送的匯出用這個 */
  blobGet: (path: string) => blobRequest(path, "GET"),

  /** multipart 上傳。⚠ 不可自行設 Content-Type，boundary 要交給瀏覽器產生 */
  upload: <T>(path: string, form: FormData) =>
    request<T>(path, { method: 'POST', body: form }),

  /**
   * 取二進位內容。
   *
   * ⚠ 檔案下載必須走這裡而不是 `<img src="/api/files/1">` ——
   * 那樣瀏覽器不會帶 Authorization 標頭，後端會回 401。
   * 這也正是「下載走 API 代理、不發簽章網址」的代價：
   * 換來的是每一次調閱都留得下紀錄。
   */
  blob: async (path: string): Promise<Blob> => {
    const token = auth.token()
    const res = await fetch(path, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    })
    if (res.status === 401) {
      auth.clear()
      window.location.href = '/admin/login'
      throw new ApiError(401, '登入已過期，請重新登入')
    }
    if (!res.ok) {
      const body = await res.json().catch(() => null)
      throw new ApiError(res.status, body?.message ?? `下載失敗（${res.status}）`, body?.code)
    }
    return await res.blob()
  },
}

export interface MenuNode {
  id: number
  name: string
  path: string | null
  icon: string | null
  sortOrder: number
  permissionCode: string | null
  isActive: boolean
  children: MenuNode[]
}

async function blobRequest(path: string, method: "GET" | "POST", body?: unknown): Promise<{ blob: Blob; filename: string; warnings: string[] }> {
    const token = auth.token()
    const res = await fetch(path, {
      method,
      headers: {
        ...(body === undefined ? {} : { "Content-Type": "application/json" }),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
    if (res.status === 401) {
      auth.clear()
      window.location.href = "/admin/login"
      throw new ApiError(401, "登入已過期，請重新登入")
    }
    if (!res.ok) {
      const b = await res.json().catch(() => null)
      throw new ApiError(res.status, b?.message ?? `匯出失敗（${res.status}）`, b?.code)
    }
    // 後端用 RFC 5987 的 filename*=UTF-8'' 帶中文檔名
    const cd = res.headers.get("content-disposition") ?? ""
    const star = /filename\*=UTF-8''([^;]+)/i.exec(cd)
    const plain = /filename="?([^";]+)"?/i.exec(cd)
    let filename = "export.docx"
    try { filename = star ? decodeURIComponent(star[1]) : plain ? plain[1] : filename } catch { /* 保留預設 */ }

    const w = res.headers.get("X-Export-Warnings")
    let warnings: string[] = []
    try { warnings = w ? decodeURIComponent(w).split(" / ").filter(Boolean) : [] } catch { warnings = [] }

    return { blob: await res.blob(), filename, warnings }
}
