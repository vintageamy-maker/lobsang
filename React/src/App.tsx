import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { searchProducts, HttpError, type BookInfo, type ProductItem, type ProductPage } from './books'
import { BookDetail, Cover } from './BookCard'
import { BarcodeScanner, canScan } from './BarcodeScanner'

/**
 * Lobsang 前台（不用登入）：**商品查詢**（2026-11-08 業主決定，前台只放商品查詢）。
 *   ・用名稱、作者、店內碼、條碼搜尋；或按「📷 掃描」用手機相機掃條碼
 *   ・只查得到「上架」的商品；價格只顯示業主自己的售價，庫存顯示實際數量（業主決定）
 *   ・預設不列出商品，要輸入關鍵字或掃碼才顯示
 * ⭐ 原本的「用 ISBN／條碼查」（查外部資料庫）和「搜尋書單」都拿掉了；書單＝採購候選，只在後台。
 * ⭐ 網址帶 ?q=… 會自動搜尋（例：從其他地方連過來）。
 */
export default function App() {
  return (
    <div className="page">
      <header className="hero">
        <h1>商品查詢系統</h1>
      </header>

      <ProductSearch initial={new URLSearchParams(location.search).get('q') ?? ''} />

      {/* 2026-11-13 業主：前台放《立體書不可思議》重點整理的連結（靜態頁 public/notes/popup-book.html） */}
      <nav className="notes-links" aria-label="延伸閱讀">
        <span>延伸閱讀</span>
        <a href="/notes/popup-book.html">📖 立體書不可思議 重點整理</a>
      </nav>
    </div>
  )
}

const money = (n?: number | null) => (n == null ? null : `NT$${n.toLocaleString()}`)

function ProductSearch({ initial }: { initial: string }) {
  const [q, setQ] = useState(initial)
  const [data, setData] = useState<ProductPage | null>(null)
  const [label, setLabel] = useState('')       // 目前結果是用什麼查的（顯示用）
  const [last, setLast] = useState<{ q?: string; code?: string }>({})
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [open, setOpen] = useState<ProductItem | null>(null)
  const [scanning, setScanning] = useState(false)
  const seq = useRef(0)

  const run = useCallback(async (opt: { q?: string; code?: string }, page = 1, autoOpen = false) => {
    const kw = opt.q?.trim()
    const code = opt.code?.trim()
    if (!kw && !code) { setData(null); setErr(null); return }
    const my = ++seq.current
    setBusy(true); setErr(null)
    try {
      const r = await searchProducts({ q: kw, code, page })
      if (my !== seq.current) return
      setData(r); setLast({ q: kw, code }); setLabel(code ? `條碼 ${code}` : `關鍵字「${kw}」`)
      // 掃碼剛好只有一筆：直接打開詳細資料，客人不用再點一次
      if (autoOpen && r.total === 1) setOpen(r.items[0])
    } catch (e) {
      if (my === seq.current) setErr(e instanceof HttpError ? e.message : '連不到伺服器，請稍後再試。')
    } finally {
      if (my === seq.current) setBusy(false)
    }
  }, [])

  useEffect(() => { if (initial) run({ q: initial }) }, [initial, run])

  const submit = (e: FormEvent) => { e.preventDefault(); run({ q }) }
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1

  return (
    <>
      <form className="search" onSubmit={submit}>
        <input value={q} onChange={e => setQ(e.target.value)} autoFocus autoComplete="off"
          placeholder="商品名稱、作者、店內碼或條碼" aria-label="搜尋商品" />
        {canScan() && (
          <button type="button" className="btn-scan" onClick={() => setScanning(true)} aria-label="用相機掃條碼">📷 掃描</button>
        )}
        <button className="btn-primary" disabled={busy}>{busy ? '搜尋中…' : '搜尋'}</button>
      </form>
      {scanning && (
        <BarcodeScanner
          onClose={() => setScanning(false)}
          onDetect={c => { setScanning(false); setQ(c); run({ code: c }, 1, true) }}
        />
      )}

      {err && <div className="msg msg-err">{err}</div>}
      {!data && !err && !busy && <p className="hint">輸入關鍵字按「搜尋」，或按「📷 掃描」掃商品上的條碼。</p>}
      {busy && !data && <div className="msg">搜尋中…</div>}

      {data && (
        <>
          <p className="hint">共 {data.total} 筆（{label}）</p>
          {data.items.length === 0 ? (
            <div className="card empty">找不到符合的商品。</div>
          ) : (
            <ul className="catalog">
              {data.items.map(p => (
                <li key={p.sku}>
                  <div className="catalog-item">
                    {/* 書封（2026-11-08 業主要求）：還沒查書補資料的顯示灰色底；點一下也能看詳細內容 */}
                    <button type="button" className="catalog-cover" onClick={() => setOpen(p)} aria-label={`看「${p.name}」的詳細內容`}>
                      <Cover covers={p.info?.covers ?? []} small />
                    </button>
                    <div className="catalog-text">
                      <strong>{p.name}</strong>
                      {p.authors && <span>作者：{p.authors}</span>}
                      <span>店內碼：{p.sku}</span>
                      <span>
                        售價：{money(p.salePrice) ? <b className="price">{money(p.salePrice)}</b> : '價格請洽店員'}
                        <span className="stock">庫存：{p.stock}</span>
                      </span>
                    </div>
                    <button className="btn-detail" onClick={() => setOpen(p)}>詳細內容</button>
                  </div>
                </li>
              ))}
            </ul>
          )}
          {pages > 1 && (
            <div className="pager">
              <button disabled={busy || data.page <= 1} onClick={() => run(last, data.page - 1)}>上一頁</button>
              <span>{data.page} / {pages}</span>
              <button disabled={busy || data.page >= pages} onClick={() => run(last, data.page + 1)}>下一頁</button>
            </div>
          )}
        </>
      )}
      {open && <ProductModal item={open} onClose={() => setOpen(null)} />}
    </>
  )
}

/**
 * 商品的詳細資料（彈窗）。外框照後台 antd Modal：標題列「詳細資料」＋右上 ✕、關閉鈕在右下。
 * ⭐ 名稱用業主清單上的商品名稱；最前面放店內碼、售價、庫存、分類，下面接查書資料（封面、作者、簡介…）。
 */
function ProductModal({ item, onClose }: { item: ProductItem; onClose: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    const prev = document.body.style.overflow
    document.body.style.overflow = 'hidden'   // 彈窗開著時後面的清單不要跟著捲
    return () => { window.removeEventListener('keydown', onKey); document.body.style.overflow = prev }
  }, [onClose])

  // ⚠ 不要把焦點放在最下面的「關閉」鈕（autoFocus）：資料長的時候瀏覽器會捲到底，一打開就看不到名稱
  const boxRef = useRef<HTMLDivElement>(null)
  useEffect(() => { boxRef.current?.focus({ preventScroll: true }) }, [])

  const info: BookInfo = {
    ...(item.info ?? { kind: 'book', code: item.code, authors: [], categories: [], covers: [], sources: [] }),
    title: item.name,
  }
  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div ref={boxRef} tabIndex={-1} className="modal" role="dialog" aria-modal="true" aria-label={`「${item.name}」的詳細內容`} onClick={e => e.stopPropagation()}>
        <div className="modal-head">
          <span>詳細資料</span>
          <button type="button" className="modal-x" aria-label="關閉" onClick={onClose}>✕</button>
        </div>
        <div className="modal-body">
          <BookDetail info={info} extraRows={[
            ['店內碼', item.sku],
            ['售價', money(item.salePrice) ?? '價格請洽店員'],
            ['庫存', String(item.stock)],
            ['商品分類', [item.midCategory, item.subCategory].filter(Boolean).join(' ／ ')],
            ['條碼', item.code],
          ]} />
        </div>
        <div className="modal-foot"><button className="btn-primary" onClick={onClose}>關閉</button></div>
      </div>
    </div>
  )
}
