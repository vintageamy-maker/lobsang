/**
 * LIFF 頁面專用的 API 包裝。
 *
 * ⚠⚠ **不可以改用 `src/api.ts`**（後台那一份），有兩個各自獨立的理由：
 *
 * 1. 它把權杖讀寫在 `localStorage`。LIFF 與後台是**同一個 origin**，
 *    共用就會互相污染 —— 在手機上開過 LIFF 之後回到後台，
 *    拿到的可能是一顆只能打幾條路由的 LINE 權杖，
 *    症狀是「後台突然到處說沒有權限」，而且完全看不出跟 LINE 有關。
 *    ⇒ 這裡的權杖**只放記憶體**，關掉頁面就沒了。
 *
 * 2. 它在 401 時 `window.location.href = '/admin/login'`。
 *    在 LINE 的內建瀏覽器裡那是一條**死路**：使用者會看到一個要帳密的登入頁，
 *    但他本來就是用 LINE 身分進來的。
 *    ⇒ 這裡 401 一律**自動重換一顆權杖再重試一次**。
 *
 * ⚠ LINE 的 ID Token 大約一小時過期。短操作看不出來，
 *   但**表單填得久，一定會踩到** —— 所以重換時要連 ID Token 一起重取，
 *   而且**畫面上已經填好的內容不可以被清掉**（不要 reload）。
 */

export interface LineSession {
  token: string
  userId: number
  name: string
  can: { overtime: boolean; leave: boolean; upload: boolean; survey: boolean; mealPhoto: boolean; correction: boolean; temperature: boolean }
  note: string | null
}

/** ⚠ 只放記憶體。理由見檔頭第 1 點。 */
let session: LineSession | null = null

/** LIFF 端點是 /admin/line/bind，API 在 /api/*，故一律以此為基準往上一層。 */
const API_BASE = `${import.meta.env.BASE_URL}../`

/** 重新跟 LINE 要一次 ID Token（舊的可能已經過期）。 */
async function freshIdToken(): Promise<string> {
  const liff = window.liff
  if (!liff) throw new Error('LIFF 尚未載入')
  if (!liff.isLoggedIn()) { liff.login(); throw new Error('正在重新登入 LINE…') }
  const t = liff.getIDToken()
  if (!t) throw new Error('無法取得 LINE 身分證明。請確認 LIFF 設定已勾選 openid 權限。')
  return t
}

/** 換一顆 15 分鐘的權杖。⚠ 每次呼叫都重取 ID Token，不要沿用開頁時那顆。 */
export async function openSession(): Promise<LineSession> {
  const idToken = await freshIdToken()
  const res = await fetch(`${API_BASE}api/line/session`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ idToken }),
  })
  const data = await res.json().catch(() => ({}))
  if (!res.ok) throw new Error(data.message ?? '無法建立連線，請回聊天室重新進入')
  session = data as LineSession
  return session
}

export function currentSession() { return session }

/**
 * 打後台 API。
 * ⚠ 401 時**只重試一次** —— 無限重試會在權杖真的拿不到時變成無聲的當機。
 */
export async function call<T>(
  path: string,
  init: { method?: string; body?: unknown } = {},
): Promise<{ ok: boolean; status: number; data: T }> {
  const send = async (token: string) => {
    const res = await fetch(`${API_BASE}${path.replace(/^\//, '')}`, {
      method: init.method ?? 'GET',
      headers: {
        Authorization: `Bearer ${token}`,
        ...(init.body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      },
      body: init.body !== undefined ? JSON.stringify(init.body) : undefined,
    })
    // 204 沒有 body
    const data = res.status === 204 ? ({} as T) : await res.json().catch(() => ({} as T))
    return { ok: res.ok, status: res.status, data }
  }

  const s = session ?? await openSession()
  const first = await send(s.token)
  if (first.status !== 401) return first

  // 權杖過期、或人已被停用／解綁 —— 重換一顆再試一次，失敗就讓呼叫端顯示訊息
  const again = await openSession()
  return send(again.token)
}

/** multipart 上傳（附件）。⚠ 不可帶 Content-Type，要讓瀏覽器自己加 boundary。 */
export async function upload(form: FormData) {
  const s = session ?? await openSession()
  const go = async (token: string) => {
    const res = await fetch(`${API_BASE}api/files`, {
      method: 'POST', headers: { Authorization: `Bearer ${token}` }, body: form,
    })
    const data = await res.json().catch(() => ({}))
    return { ok: res.ok, status: res.status, data }
  }
  const first = await go(s.token)
  if (first.status !== 401) return first
  const again = await openSession()
  return go(again.token)
}
