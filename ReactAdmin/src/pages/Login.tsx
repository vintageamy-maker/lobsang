import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { auth } from '../api'

/**
 * ⚠⚠ 快速登入的帳密**不再寫在這裡**（2026-08-24）。
 *
 * 原本這個檔案裡有一份 `QUICK_ACCOUNTS`，含 admin 與五個測試帳號的明文密碼。
 * 前端 bundle 是**公開下載得到的**，而開發站與正式站的測試帳號密碼**是同一組** ——
 * ⇒ 只把按鈕從正式站藏起來完全沒有用：任何人下載開發站的 js 就拿得到密碼，
 *   那組密碼在正式站照樣登得進去。
 * ⭐ 判準：**能被下載的東西裡不可以有密碼。**「畫面上看不到」不等於「拿不到」。
 *
 * 現在改成向後端要：開發站的伺服器設定裡有這份清單就回傳，正式站永遠回空陣列
 * ⇒ 整區不顯示。⭐ 兩站跑的是**同一份程式**，差別只在伺服器上的設定 ——
 *   建置兩份不同的檔案會讓「開發站測過的那包」不等於「上正式站的那包」。
 */
interface DemoAccount { label: string; account: string; password: string }

export default function Login() {
  const navigate = useNavigate()
  const [account, setAccount] = useState('')
  const [password, setPassword] = useState('')
  const [showPwd, setShowPwd] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [demos, setDemos] = useState<DemoAccount[]>([])

  // ⚠ 拿不到就當作沒有（正式站本來就會回空陣列）—— 失敗不可以影響登入本身
  useEffect(() => {
    fetch('/api/auth/demo-accounts')
      .then(r => (r.ok ? r.json() : []))
      .then((d: DemoAccount[]) => setDemos(Array.isArray(d) ? d : []))
      .catch(() => setDemos([]))
  }, [])

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (busy) return
    setBusy(true); setMsg(null)
    try {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ account, password }),
      })
      if (!res.ok) {
        const err = await res.json().catch(() => null)
        setMsg(err?.message ?? `登入失敗（${res.status}）`)
        return
      }
      const data = await res.json()
      /*
       * ⚠⚠ `mustChangePassword` 在回應的**最外層**，不在 `data.user` 裡面 ——
       *    直接 `auth.save(data.token, data.user)` 會把這個旗標丟掉，
       *    強制換密碼就永遠不會觸發（2026-08-22 接這個機制時發現）。
       */
      auth.save(data.token, { ...data.user, mustChangePassword: !!data.mustChangePassword })
      navigate('/', { replace: true })
    } catch {
      setMsg('無法連線到伺服器，請稍後再試')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-page">
      <div className="login-panel">
        <aside className="brand-side">
          <div className="brand-badge">Lobsang · Admin</div>
          <h1>Lobsang<br />後台管理系統</h1>
          <p>後台管理入口</p>
          <ul className="brand-points">
            <li>使用者與權限管理</li>
            <li>組織編制與簽核流程</li>
            <li>表單填寫與簽核</li>
          </ul>
        </aside>

        <main className="form-side">
          <h2>管理員登入</h2>
          <p className="form-hint">請使用管理員帳號登入後台</p>

          <form onSubmit={submit}>
            <label>
              帳號
              <input value={account} onChange={e => setAccount(e.target.value)}
                placeholder="請輸入帳號" autoComplete="username" required />
            </label>

            <label>
              密碼
              <div className="pwd-wrap">
                <input type={showPwd ? 'text' : 'password'} value={password}
                  onChange={e => setPassword(e.target.value)}
                  placeholder="請輸入密碼" autoComplete="current-password" required />
                <button type="button" className="pwd-toggle" onClick={() => setShowPwd(v => !v)}>
                  {showPwd ? '隱藏' : '顯示'}
                </button>
              </div>
            </label>

            <button type="submit" className="btn-login" disabled={!account || !password || busy}>
              {busy ? '登入中…' : '登入'}
            </button>

            {msg && <div className="notice">{msg}</div>}
          </form>

          {/* ⚠ 清單由後端給：正式站永遠回空陣列 ⇒ 這整區不會出現 */}
          {demos.length > 0 && (
            <div className="quick-login">
              <div className="quick-title">測試環境 · 快速登入</div>
              <div className="quick-btns">
                {demos.map(q => (
                  <button key={q.account} type="button" className="quick-btn"
                    onClick={() => { setAccount(q.account); setPassword(q.password); setMsg(null) }}>
                    {q.label}
                  </button>
                ))}
              </div>
            </div>
          )}

          <a className="back-link" href="/">← 返回主站</a>
        </main>
      </div>

      <footer className="login-foot">Lobsang</footer>
    </div>
  )
}
