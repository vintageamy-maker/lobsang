import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { BookInfo, LinkItem, SourceStatus } from './books'

/** 封面：依序試每一張，載入失敗或是 1x1 的空圖就換下一張 */
export function Cover({ covers, small }: { covers: string[]; small?: boolean }) {
  const [i, setI] = useState(0)
  useEffect(() => setI(0), [covers])
  const src = covers[i]
  return (
    <div className={small ? 'cover cover-sm' : 'cover'}>
      {src ? (
        <img
          key={src}
          src={src}
          alt="封面"
          referrerPolicy="no-referrer"
          onLoad={e => { if ((e.target as HTMLImageElement).naturalWidth < 10) setI(i + 1) }}
          onError={() => setI(i + 1)}
        />
      ) : (small ? null : '沒有封面')}
    </div>
  )
}

export function StatusChips({ statuses }: { statuses: SourceStatus[] }) {
  if (!statuses.length) return null
  return (
    <div className="chips">
      {statuses.map(s => (
        <span key={s.name} className={`chip ${s.state === 'ok' ? 'chip-ok' : s.state === 'err' ? 'chip-warn' : ''}`}>
          {s.name}{s.state === 'ok' ? ' ✓ 有資料' : s.state === 'none' ? ' — 查無資料' : ` ⚠ ${s.note ?? '失敗'}`}
        </span>
      ))}
    </div>
  )
}

export function Links({ title, links }: { title: string; links: LinkItem[] }) {
  if (!links.length) return null
  return (
    <div className="links">
      {title}
      {links.map(l => <a key={l.url} href={l.url} target="_blank" rel="noopener noreferrer">{l.label}</a>)}
    </div>
  )
}

function rowsOf(b: BookInfo): [string, string | null | undefined][] {
  if (b.kind === 'product') {
    return [
      ['品牌', b.brand], ['規格', b.quantity], ['定價', b.price], ['分類', b.categories.join('、')],
      ['銷售地區', b.countries], ['條碼註冊地', b.gs1 ? `${b.gs1}（廠商申請條碼的地方，不一定是產地）` : null],
      ['條碼', b.code], ['資料來源', b.sources.join('、')],
    ]
  }
  return [
    ['作者', b.authors.join('、')], ['出版社', b.publisher], ['出版日期', b.date], ['版次', b.edition],
    ['頁數', b.pages ? `${b.pages} 頁` : null], ['定價', b.price], ['語言', b.language],
    ['分類', b.categories.join('、')], ['ISBN-13', b.code], ['ISBN-10', b.isbn10],
    ['產地判斷', b.regionLabel], ['資料來源', b.sources.join('、')],
  ]
}

/**
 * 一本書／一個商品的詳細資料（放在「詳細內容」彈窗裡）。
 * ⭐ 2026-11-07 業主要求：版面照後台「詳細資料」彈窗——左邊小封面，右邊書名＋一行一項資料，
 *    簡介先顯示 3 行（按「展開」看全部），目次、成分可點開。查詢來源、其他網站連結不放。
 */
export function BookDetail({ info }: { info: BookInfo }) {
  const rows = rowsOf(info).filter((r): r is [string, string] => !!r[1])
  return (
    <div className="dt">
      <div className="dt-top">
        <div className="dt-cover"><Cover covers={info.covers} /></div>
        <div className="dt-main">
          <h3 className="dt-title">{info.title || (info.kind === 'product' ? '（無名稱）' : '（無書名）')}</h3>
          {info.subtitle && <div className="dt-sub">{info.subtitle}</div>}
          <dl className="dt-rows">
            {rows.map(([k, v]) => [<dt key={k + 't'}>{k}</dt>, <dd key={k + 'd'}>{v}</dd>])}
          </dl>
          {info.description && <Clamp text={info.description} />}
        </div>
      </div>
      {info.ingredients && <details className="dt-more"><summary>成分</summary><div>{info.ingredients}</div></details>}
      {info.toc && <details className="dt-more"><summary>目次</summary><div>{info.toc.replace(/^目次\s*/, '')}</div></details>}
    </div>
  )
}

/** 長文字先顯示 3 行，超過才出現「展開」（跟後台 antd 的 ellipsis expandable 一樣） */
function Clamp({ text }: { text: string }) {
  const ref = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [overflow, setOverflow] = useState(false)
  useLayoutEffect(() => {
    const el = ref.current
    if (el && !open) setOverflow(el.scrollHeight > el.clientHeight + 1)
  }, [text, open])
  return (
    <div className="dt-desc">
      <div ref={ref} className={open ? '' : 'dt-clamp'}>{text}</div>
      {overflow && !open && <button type="button" className="dt-link" onClick={() => setOpen(true)}>展開</button>}
    </div>
  )
}
