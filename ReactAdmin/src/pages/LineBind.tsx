import { useEffect, useState } from 'react'
import { S } from './line/styles'

/**
 * LINE 帳號綁定頁（LIFF）。
 *
 * ⚠ **這一頁刻意不在後台框架（AdminLayout）裡，也不需要登入** ——
 *    綁定的當下使用者本來就還沒登入，那正是要解決的問題。
 *    路由註冊在 /login 同一層。
 *
 * ⚠ 這一頁在 **LINE 的內建瀏覽器**裡開，不是一般瀏覽器：
 *    - 畫面很窄，一切以單欄、大按鈕為主
 *    - 不要用 antd —— 整包載進來只為了一個表單，在行動網路下很慢。用原生元素。
 *
 * ⚠ 不重複使用後台的登入 API：那支會發權杖並寫登入紀錄，
 *    而這裡只是「證明你是本人」，不該產生一次登入。
 *
 * ⭐ 之後要在 LINE 裡做其他功能（送單、查詢…），用 ./line/lineApi.ts 的 openSession
 *    換一顆 LINE 專用的短命權杖（POST /api/line/session），不要沿用這一頁的 ID Token。
 */

// ⚠ LIFF SDK 只能從 LINE 的 CDN 載，且必須在 liff.init 之前載完
const LIFF_SDK = 'https://static.line-scdn.net/liff/edge/2/sdk.js'

type Phase = 'loading' | 'form' | 'bound' | 'done' | 'error'

interface BindStatus {
  bound: boolean; name: string | null; employeeNo: string | null; active: boolean | null
}

interface Liff {
  init(c: { liffId: string }): Promise<void>
  isLoggedIn(): boolean
  login(): void
  getIDToken(): string | null
  closeWindow(): void
  isInClient(): boolean
}
declare global { interface Window { liff?: Liff } }

const loadScript = (src: string) => new Promise<void>((resolve, reject) => {
  if (document.querySelector(`script[src="${src}"]`)) return resolve()
  const s = document.createElement('script')
  s.src = src
  s.onload = () => resolve()
  s.onerror = () => reject(new Error('載入 LINE 元件失敗'))
  document.head.appendChild(s)
})

export default function LineBind() {
  const [phase, setPhase] = useState<Phase>('loading')
  const [error, setError] = useState('')
  const [idToken, setIdToken] = useState<string | null>(null)
  const [account, setAccount] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<{ name: string; already: boolean; unbound?: boolean } | null>(null)
  const [inClient, setInClient] = useState(true)
  const [status, setStatus] = useState<BindStatus | null>(null)

  const api = (path: string, body: unknown) =>
    fetch(`${import.meta.env.BASE_URL}../api/line/${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })

  useEffect(() => {
    (async () => {
      try {
        // LIFF ID 由後端給 —— 前端寫死的話，換 LIFF 要重新建置前端
        const cfg = await fetch(`${import.meta.env.BASE_URL}../api/line/bind-config`)
          .then(r => r.json()) as { liffId: string | null; configured: boolean }
        if (!cfg.configured || !cfg.liffId) {
          setError('系統尚未設定 LIFF，請聯絡管理員'); setPhase('error'); return
        }

        await loadScript(LIFF_SDK)
        const liff = window.liff!
        await liff.init({ liffId: cfg.liffId })
        setInClient(liff.isInClient())

        if (!liff.isLoggedIn()) { liff.login(); return }

        const token = liff.getIDToken()
        if (!token) {
          // ⚠ 常見原因：LIFF 的 Scopes 沒勾 openid
          setError('無法取得 LINE 身分證明。請確認 LIFF 設定已勾選 openid 權限。')
          setPhase('error'); return
        }
        setIdToken(token)

        // ⭐ 先問這個 LINE 綁到誰：已綁定就不要再叫人輸入一次帳密
        const st = await api('bind-status', { idToken: token }).then(r => r.json()) as BindStatus
        setStatus(st)
        setPhase(st.bound ? 'bound' : 'form')
      } catch (e) {
        setError(e instanceof Error ? e.message : '初始化失敗')
        setPhase('error')
      }
    })()
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const submit = async (ev: React.FormEvent) => {
    ev.preventDefault()
    if (busy) return
    setBusy(true); setError('')
    try {
      const res = await api('bind', { idToken, account, password })
      const data = await res.json().catch(() => ({}))
      if (!res.ok) { setError(data.message ?? '綁定失敗'); return }
      setResult({ name: data.name, already: data.already })
      setPhase('done')
    } catch {
      setError('連線失敗，請確認網路後再試一次')
    } finally {
      setBusy(false)
    }
  }

  /** 解除綁定。⚠ 刻意要二次確認 —— 解錯了要重新輸入帳密才綁得回來 */
  const unbind = async () => {
    if (busy) return
    if (!confirm(`確定要解除「${status?.name}」的綁定嗎？\n解除後要重新輸入帳號密碼才能再綁定。`)) return
    setBusy(true); setError('')
    try {
      const res = await api('unbind', { idToken })
      const data = await res.json().catch(() => ({}))
      if (!res.ok) { setError(data.message ?? '解除失敗'); return }
      setResult({ name: status?.name ?? '', already: false, unbound: true })
      setPhase('done')
    } catch {
      setError('連線失敗，請確認網路後再試一次')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div style={S.page}>
      <div style={S.card}>
        <div style={S.brand}>Lobsang</div>
        <h1 style={S.title}>綁定 LINE 帳號</h1>

        {phase === 'loading' && <p style={S.hint}>正在取得 LINE 身分…</p>}

        {phase === 'error' && (
          <>
            <div style={S.alertErr}>{error}</div>
            <button style={S.btnGhost} onClick={() => location.reload()}>重新載入</button>
          </>
        )}

        {phase === 'bound' && status && (
          <>
            <div style={S.alertOk}>
              目前已綁定
              <div style={S.who}>{status.name}</div>
              {status.employeeNo && <div style={S.small}>員工編號 {status.employeeNo}</div>}
            </div>
            {/* ⚠ 停用者仍看得到並可解綁 —— 「不能用功能」與「綁在誰身上」是兩件事 */}
            {status.active === false && (
              <div style={S.alertErr}>
                這個帳號目前是<b>停用</b>狀態，無法使用 LINE 的功能。如需恢復請洽管理員。
              </div>
            )}
            <p style={S.hint}>
              換手機或換 LINE 帳號時，請先在這裡解除綁定，再用新的 LINE 綁一次。
            </p>
            {error && <div style={S.alertErr}>{error}</div>}
            <button style={{ ...S.btnDanger, ...(busy ? S.btnBusy : null) }}
              onClick={unbind} disabled={busy}>
              {busy ? '解除中…' : '解除綁定'}
            </button>
            {inClient && (
              <button style={{ ...S.btnGhost, marginTop: 10 }}
                onClick={() => window.liff?.closeWindow()}>關閉</button>
            )}
            <p style={S.small}>手機遺失導致無法自行解除時，請洽管理員由後台解除。</p>
          </>
        )}

        {phase === 'form' && (
          <form onSubmit={submit}>
            <p style={S.hint}>
              請輸入你在系統的帳號密碼，確認身分後就會與這個 LINE 帳號綁定。
            </p>
            <label style={S.label}>
              帳號
              <input style={S.input} value={account} autoComplete="username"
                onChange={e => setAccount(e.target.value)} required
                autoCapitalize="off" autoCorrect="off" />
            </label>
            <label style={S.label}>
              密碼
              <input style={S.input} type="password" value={password}
                autoComplete="current-password"
                onChange={e => setPassword(e.target.value)} required />
            </label>
            {error && <div style={S.alertErr}>{error}</div>}
            <button style={{ ...S.btn, ...(busy ? S.btnBusy : null) }} type="submit" disabled={busy}>
              {busy ? '綁定中…' : '確認綁定'}
            </button>
            <p style={S.small}>忘記帳號或密碼請洽管理員。</p>
          </form>
        )}

        {phase === 'done' && result && (
          <>
            <div style={result.unbound ? S.alertWarn : S.alertOk}>
              {result.unbound ? '已解除綁定' : result.already ? '這個 LINE 已經綁定過了' : '綁定成功'}
              <div style={S.who}>{result.name}</div>
            </div>
            <p style={S.hint}>
              {result.unbound ? '之後要再使用，請重新綁定。' : '回到聊天室，點下方選單就可以開始使用。'}
            </p>
            {result.unbound && (
              <button style={S.btnGhost} onClick={() => location.reload()}>重新綁定</button>
            )}
            {inClient && (
              <button style={S.btn} onClick={() => window.liff?.closeWindow()}>關閉</button>
            )}
          </>
        )}
      </div>
    </div>
  )
}
