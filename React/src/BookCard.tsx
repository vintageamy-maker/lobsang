import { useEffect, useState, type ReactNode } from 'react'
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
      ['品牌', b.brand], ['規格', b.quantity], ['分類', b.categories.join('、')],
      ['銷售地區', b.countries], ['條碼註冊地', b.gs1 ? `${b.gs1}（廠商申請條碼的地方，不一定是產地）` : null],
      ['條碼', b.code], ['資料來源', b.sources.join('、')],
    ]
  }
  return [
    ['作者', b.authors.join('、')], ['出版社', b.publisher], ['出版日期', b.date], ['版次', b.edition],
    ['頁數', b.pages ? `${b.pages} 頁` : null], ['價格', b.price], ['語言', b.language],
    ['分類', b.categories.join('、')], ['ISBN-13', b.code], ['ISBN-10', b.isbn10],
    ['產地判斷', b.regionLabel], ['資料來源', b.sources.join('、')],
  ]
}

/** 一本書／一個商品的詳細資料 */
export function BookDetail({ info, badge, children }: { info: BookInfo; badge?: ReactNode; children?: ReactNode }) {
  const rows = rowsOf(info).filter((r): r is [string, string] => !!r[1])
  return (
    <div className="card">
      <div className="book">
        <Cover covers={info.covers} />
        <div className="book-main">
          {badge}
          <h2>{info.title || (info.kind === 'product' ? '（無名稱）' : '（無書名）')}</h2>
          {info.subtitle && <div className="subtitle">{info.subtitle}</div>}
          <dl className="info">
            {rows.map(([k, v]) => [<dt key={k + 't'}>{k}</dt>, <dd key={k + 'd'}>{v}</dd>])}
          </dl>
          {children}
        </div>
      </div>
      {info.description && <div className="desc">{info.description}</div>}
      {info.ingredients && <details className="desc"><summary>成分</summary>{info.ingredients}</details>}
      {info.toc && <details className="desc"><summary>目次</summary>{info.toc.replace(/^目次\s*/, '')}</details>}
    </div>
  )
}
