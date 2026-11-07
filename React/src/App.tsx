import { useCallback, useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { lookupCode, searchCatalog, HttpError, type BookInfo, type CatalogItem, type LookupResult } from './books'
import { BookDetail, Links, StatusChips } from './BookCard'
import { esliteCode, fetchEslite, mergeEslite } from './eslite'
import { BarcodeScanner, canScan } from './BarcodeScanner'

type Tab = 'lookup' | 'catalog'

/**
 * Lobsang 主站：公開的查書頁（不用登入）。
 *   ・用 ISBN／條碼查書：查國圖、Google Books、openBD 等公開資料庫
 *   ・搜尋書單：業主在後台建立的書單
 * ⭐ 網址帶 ?isbn=… 會自動查詢；?tab=catalog 直接開書單。
 */
export default function App() {
  const params = new URLSearchParams(location.search)
  const [tab, setTab] = useState<Tab>(params.get('tab') === 'catalog' ? 'catalog' : 'lookup')
  return (
    <div className="page">
      <header className="hero">
        <div className="hero-badge">Lobsang</div>
        <h1>Search</h1>
        <p className="sub">輸入 ISBN 或商品條碼查詢資料，也可以搜尋我們的書單</p>
      </header>

      <nav className="tabs" role="tablist">
        <button role="tab" aria-selected={tab === 'lookup'} className={tab === 'lookup' ? 'active' : ''} onClick={() => setTab('lookup')}>用 ISBN／條碼查</button>
        <button role="tab" aria-selected={tab === 'catalog'} className={tab === 'catalog' ? 'active' : ''} onClick={() => setTab('catalog')}>搜尋書單</button>
      </nav>

      {tab === 'lookup' ? <LookupView initial={params.get('isbn') ?? ''} /> : <CatalogView />}

      <footer className="foot">Lobsang · <a href="/admin/">後台</a></footer>
    </div>
  )
}

function LookupView({ initial }: { initial: string }) {
  const [code, setCode] = useState(initial)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [res, setRes] = useState<{ result: LookupResult; inList: boolean } | null>(null)
  const [scanning, setScanning] = useState(false)
  const [detailOpen, setDetailOpen] = useState(false)
  const [esliteBusy, setEsliteBusy] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)
  const seq = useRef(0)

  const run = useCallback(async (raw: string) => {
    if (!raw.trim()) return
    const my = ++seq.current
    setBusy(true); setErr(null); setRes(null); setDetailOpen(false); setEsliteBusy(false)
    // ⭐ 誠品在瀏覽器查（見 eslite.ts），跟伺服器的查詢同時出發；伺服器結果先顯示，誠品回來再補上
    const isbn = esliteCode(raw)   // ISBN 或商品條碼（誠品也賣商品）
    const eslite = isbn ? fetchEslite(isbn) : null
    let ok = false
    try {
      const r = await lookupCode(raw)
      if (my === seq.current) { setRes(r); ok = true }
    } catch (e) {
      if (my === seq.current) setErr(e instanceof HttpError ? e.message : '連不到伺服器，請稍後再試。')
    } finally {
      if (my === seq.current) { setBusy(false); inputRef.current?.select() }
    }
    if (!ok || !eslite || !isbn) return
    setEsliteBusy(true)
    const outcome = await eslite
    if (my !== seq.current) return
    setEsliteBusy(false)
    setRes(prev => prev ? { ...prev, result: mergeEslite(prev.result, isbn, outcome) } : prev)
  }, [])

  useEffect(() => { if (initial) run(initial) }, [initial, run])

  const submit = (e: FormEvent) => { e.preventDefault(); run(code) }
  const r = res?.result

  return (
    <>
      <form className="search" onSubmit={submit}>
        <input
          ref={inputRef} value={code} onChange={e => setCode(e.target.value)} autoFocus
          placeholder="ISBN 或商品條碼，例如 9786264186421" inputMode="numeric" autoComplete="off"
          aria-label="ISBN 或商品條碼"
        />
        {canScan() && (
          <button type="button" className="btn-scan" onClick={() => setScanning(true)} aria-label="用相機掃條碼">📷 掃描</button>
        )}
        <button className="btn-primary" disabled={busy}>{busy ? '查詢中…' : '查詢'}</button>
      </form>
      {scanning && (
        <BarcodeScanner
          onClose={() => setScanning(false)}
          onDetect={c => { setScanning(false); setCode(c); run(c) }}
        />
      )}

      {busy && <div className="msg">查詢中…（同時查好幾個資料庫）</div>}
      {esliteBusy && <div className="hint">還在查誠品線上，查到會自動補上資料…</div>}
      {err && <div className="msg msg-err">{err}</div>}

      {r && r.error && <div className="msg msg-err">{r.error}</div>}
      {r && r.notes.length > 0 && <div className="note">{r.notes.join('；')}。</div>}

      {r && r.card && (
        <div className="card">
          <h2>{r.card.title}</h2>
          {r.card.message && <p className="subtitle">{r.card.message}</p>}
          <dl className="info">{r.card.rows.map(([k, v]) => [<dt key={k + 't'}>{k}</dt>, <dd key={k + 'd'}>{v}</dd>])}</dl>
          <Links title="查詢：" links={r.card.links} />
        </div>
      )}

      {/* ⭐ 跟「搜尋書單」一樣：先只顯示摘要，全部資料按「詳細內容」用彈窗看 */}
      {r && !r.error && !r.card && r.found && r.info && (
        <BookSummary
          title={r.info.title ?? r.code ?? ''}
          authors={r.info.kind === 'product' ? r.info.brand : r.info.authors.join('、')}
          authorLabel={r.info.kind === 'product' ? '品牌' : '作者'}
          publisher={r.info.publisher}
          price={r.info.price}
          badge={res?.inList ? <span className="badge">在書單裡</span> : null}
          onDetail={() => setDetailOpen(true)}
        />
      )}
      {detailOpen && r?.info && (
        <DetailModal info={r.info} onClose={() => setDetailOpen(false)} />
      )}

      {r && !r.error && !r.card && !r.found && (
        <div className="card">
          <div className="msg msg-err">{r.kind === 'product' ? `查不到這個商品（條碼 ${r.code}）。` : `查不到這本書（ISBN ${r.code}）。`}</div>
          {r.info?.gs1 && <p>條碼註冊地：{r.info.gs1}（代表廠商在哪裡申請條碼，不一定是產地）</p>}
          {r.kind === 'product' && <p className="hint">免費的商品條碼資料庫收錄有限，台灣商品尤其少。可以用下面的連結搜尋看看。</p>}
          <StatusChips statuses={r.statuses} />
          <Links title="其他網站：" links={r.links} />
        </div>
      )}
    </>
  )
}

function CatalogView() {
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<{ total: number; pageSize: number; items: CatalogItem[] } | null>(null)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [open, setOpen] = useState<CatalogItem | null>(null)
  const [searched, setSearched] = useState('')   // 目前結果用的關鍵字（輸入框還在打字時不跟著變）

  // ⭐ 2026-11-07 業主要求：預設不列出書單，要輸入關鍵字按「搜尋」才顯示結果；空白不搜尋（不會列出全部）
  const load = useCallback(async (query: string, p: number) => {
    const kw = query.trim()
    if (!kw) { setData(null); setSearched(''); setErr(null); return }
    setBusy(true); setErr(null)
    try {
      setData(await searchCatalog(kw, p))
      setSearched(kw)
      setPage(p)
    } catch (e) {
      setErr(e instanceof HttpError ? e.message : '連不到伺服器，請稍後再試。')
    } finally { setBusy(false) }
  }, [])

  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1

  return (
    <>
      <form className="search" onSubmit={e => { e.preventDefault(); load(q, 1) }}>
        <input value={q} onChange={e => setQ(e.target.value)} placeholder="書名、作者、出版社或 ISBN" aria-label="搜尋書單" autoFocus />
        <button className="btn-primary" disabled={busy}>{busy ? '搜尋中…' : '搜尋'}</button>
      </form>
      {err && <div className="msg msg-err">{err}</div>}
      {!data && !err && <p className="hint">輸入書名、作者、出版社或 ISBN，按「搜尋」查詢我們的書單。</p>}
      {data && (
        <>
          <p className="hint">共 {data.total} 筆（關鍵字「{searched}」）</p>
          {data.items.length === 0 ? (
            <div className="card empty">書單裡沒有符合的書。</div>
          ) : (
            // ⭐ 清單只放書名、作者、出版社、價格；其他資料按「詳細內容」用彈窗看
            <ul className="catalog">
              {data.items.map(b => (
                <li key={b.code}>
                  <BookSummary title={b.title} authors={b.authors} publisher={b.publisher} price={b.info?.price}
                    onDetail={b.info ? () => setOpen(b) : undefined} />
                </li>
              ))}
            </ul>
          )}
          {pages > 1 && (
            <div className="pager">
              <button disabled={busy || page <= 1} onClick={() => load(searched, page - 1)}>上一頁</button>
              <span>{page} / {pages}</span>
              <button disabled={busy || page >= pages} onClick={() => load(searched, page + 1)}>下一頁</button>
            </div>
          )}
        </>
      )}
      {open?.info && <DetailModal info={open.info} onClose={() => setOpen(null)} />}
    </>
  )
}

/**
 * 一本書的摘要：只放書名、作者、出版社、定價，其他資料按「詳細內容」看。
 * 「搜尋書單」的清單和「用 ISBN 查」的結果共用，兩邊長得一樣。
 */
function BookSummary({ title, authors, publisher, price, authorLabel = '作者', badge, onDetail }: {
  title: string
  authors?: string | null
  publisher?: string | null
  price?: string | null
  authorLabel?: string
  badge?: ReactNode
  onDetail?: () => void
}) {
  return (
    <div className="catalog-item">
      <div className="catalog-text">
        {badge}
        <strong>{title}</strong>
        {authors && <span>{authorLabel}：{authors}</span>}
        {publisher && <span>出版社：{publisher}</span>}
        <span>定價：{price ? <b className="price">{price}</b> : '—'}</span>
      </div>
      {onDetail && <button className="btn-detail" onClick={onDetail}>詳細內容</button>}
    </div>
  )
}

/**
 * 書的全部資料（彈窗）。按背景、右上 ✕、「關閉」或 Esc 都會關。
 * ⭐ 外框照後台 antd Modal：標題列「詳細資料」＋右上 ✕、關閉鈕在右下。查詢來源、其他網站不放（業主 2026-11-07 決定）
 */
function DetailModal({ info, onClose }: { info: BookInfo; onClose: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    const prev = document.body.style.overflow
    document.body.style.overflow = 'hidden'   // 彈窗開著時後面的清單不要跟著捲
    return () => { window.removeEventListener('keydown', onKey); document.body.style.overflow = prev }
  }, [onClose])

  // ⚠ 不要把焦點放在最下面的「關閉」鈕（autoFocus）：資料長的時候瀏覽器會捲到底，一打開就看不到書名
  const boxRef = useRef<HTMLDivElement>(null)
  useEffect(() => { boxRef.current?.focus({ preventScroll: true }) }, [])

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div ref={boxRef} tabIndex={-1} className="modal" role="dialog" aria-modal="true" aria-label={`「${info.title ?? ''}」的詳細內容`} onClick={e => e.stopPropagation()}>
        <div className="modal-head">
          <span>詳細資料</span>
          <button type="button" className="modal-x" aria-label="關閉" onClick={onClose}>✕</button>
        </div>
        <div className="modal-body"><BookDetail info={info} /></div>
        <div className="modal-foot"><button className="btn-primary" onClick={onClose}>關閉</button></div>
      </div>
    </div>
  )
}
