/*
 * 誠品線上：在「使用者自己的瀏覽器」查一本書，合併進查書結果。
 *
 * ⚠ 同一份檔案在 React/src 和 ReactAdmin/src/components 各放一份（兩個前端分開建置），改一邊要同步另一邊。
 * ⚠ 為什麼不在後端（Services/BookLookup）查：誠品整站有 Cloudflare 機器人防護，伺服器端程式一律 403，
 *    業主決定不偽裝、不繞過。誠品的 API（athena.eslite.com）回 Access-Control-Allow-Origin: *，
 *    本來就允許網頁跨站呼叫，而且這裡是真人查一本才問一次，量很小。
 * ⚠ 這是誠品前端自己用的 API，沒有文件，隨時可能改：任何錯誤（403、CORS、逾時、格式變了）都安靜略過，
 *    只在「查詢來源」標成查不到／失敗，不能讓整個查書失敗。
 * ⭐ 合併原則（2026-11-07 業主決定）：誠品是第 1 順位，誠品有值就用誠品的，沒有的欄位才用其他來源；價格用「定價」。
 */

const API = 'https://athena.eslite.com/api'
const TIMEOUT_MS = 15000   // 誠品偶爾要十幾秒才回

export interface EsliteBook {
  id: string
  url: string
  title: string
  subtitle?: string
  authors: string[]
  publisher?: string
  date?: string
  pages?: number
  listPrice?: number   // 定價
  language?: string
  categories: string[]
  description?: string
  toc?: string
  authorIntro?: string
  cover?: string
}

/** 誠品查詢的結果：book=null 代表誠品沒有這本；error 代表連不上或格式不對 */
export type EsliteOutcome = { book: EsliteBook | null } | { error: string }

/** 把輸入轉成 ISBN-13（不是 ISBN 就回 null，不去問誠品） */
export function toIsbn13(raw: string): string | null {
  const s = raw.replace(/[\s-]/g, '').toUpperCase()
  if (/^97[89]\d{10}$/.test(s)) return s
  if (/^\d{9}[\dX]$/.test(s)) {
    const body = '978' + s.slice(0, 9)
    const sum = [...body].reduce((a, d, i) => a + Number(d) * (i % 2 ? 3 : 1), 0)
    return body + ((10 - (sum % 10)) % 10)
  }
  return null
}

async function getJson(url: string, signal: AbortSignal) {
  const r = await fetch(url, { signal, credentials: 'omit' })
  if (!r.ok) throw new Error(`HTTP ${r.status}`)
  return r.json()
}

/** 誠品的簡介是 HTML，轉成純文字（DOMParser 不會執行裡面的程式碼） */
function htmlToText(html?: string | null): string | undefined {
  if (!html) return undefined
  const withBreaks = html.replace(/<br\s*\/?>/gi, '\n').replace(/<\/(p|div|li|h\d)>/gi, '\n')
  const text = new DOMParser().parseFromString(withBreaks, 'text/html').body.textContent ?? ''
  const clean = text.replace(/\r/g, '').replace(/[ \t ]+\n/g, '\n').replace(/\n{3,}/g, '\n\n').trim()
  return clean || undefined
}

const nonEmpty = (v: unknown): string | undefined => (typeof v === 'string' && v.trim() ? v.trim() : undefined)

export async function fetchEslite(isbn13: string): Promise<EsliteOutcome> {
  const ctl = new AbortController()
  const timer = window.setTimeout(() => ctl.abort(), TIMEOUT_MS)
  try {
    const s = await getJson(`${API}/v2/search?q=${isbn13}&size=5&start=0`, ctl.signal)
    // ⚠ 搜尋是關鍵字比對，要確認 ISBN 真的相同，不然可能拿到別本
    const hit = (s?.hits?.hit ?? []).find((h: { fields?: { isbn?: string; ean?: string } }) =>
      h.fields?.isbn === isbn13 || h.fields?.ean === isbn13)
    if (!hit) return { book: null }

    const p = await getJson(`${API}/v1/products/${encodeURIComponent(hit.id)}`, ctl.signal)
    const spec = (tag: string) => {
      const v = (p.product_specifications ?? []).find((x: { tag?: string }) => x.tag === tag)?.name as string | undefined
      return v ? v.replace(/^[^:]*:/, '').trim() : undefined   // "1:中文/繁體" → "中文/繁體"
    }
    const desc = (name: string) =>
      htmlToText((p.descriptions ?? []).find((d: { name?: string }) => d.name === name)?.description)
    const photos: { id?: number; large_path?: string }[] = p.photos ?? []
    const photo = photos.find(x => x.id === p.default_product_photo_id) ?? photos[0]
    const people = [
      nonEmpty(p.author),
      nonEmpty(p.painter) && `${p.painter.trim()}（繪）`,
      nonEmpty(p.translator) && `${p.translator.trim()}（譯）`,
    ].filter((v): v is string => !!v)
    const pages = Number.parseInt(spec('pages') ?? '', 10)
    // ⚠ v1 商品 API：final_price＝定價、retail_price＝售價（跟搜尋 API 的命名相反）
    const listPrice = Number(p.final_price)

    return {
      book: {
        id: String(hit.id),
        url: `https://www.eslite.com/product/${encodeURIComponent(hit.id)}`,
        title: nonEmpty(p.name) ?? isbn13,
        subtitle: nonEmpty(p.sub_title),
        authors: [...new Set(people)],
        publisher: nonEmpty(p.supplier),
        date: nonEmpty(p.manufacturer_date)?.slice(0, 10),
        pages: Number.isFinite(pages) && pages > 0 ? pages : undefined,
        listPrice: Number.isFinite(listPrice) && listPrice > 0 ? listPrice : undefined,
        language: spec('language'),
        // 第二層常是促銷標籤（例：「📌文學館79折起」），濾掉
        categories: [p.level1_name, p.level2_name, p.level3_name]
          .map(nonEmpty).filter((v): v is string => !!v && !/折|📌|\p{Extended_Pictographic}/u.test(v)),
        description: desc('內容簡介') ?? htmlToText(p.short_description),
        toc: desc('產品目錄'),
        authorIntro: desc('作者介紹'),
        cover: nonEmpty(photo?.large_path),
      },
    }
  } catch (e) {
    // fetch 被擋（CORS、網路斷線）時是 TypeError「Failed to fetch」，改成中文
    return { error: ctl.signal.aborted ? '逾時' : e instanceof TypeError ? '連不上' : e instanceof Error ? e.message : '連不上' }
  } finally {
    window.clearTimeout(timer)
  }
}

// ---------- 合併進查書結果 ----------

interface InfoLike {
  kind: 'book' | 'product'
  code: string
  title?: string | null
  subtitle?: string | null
  authors: string[]
  publisher?: string | null
  date?: string | null
  pages?: number | null
  price?: string | null
  categories: string[]
  language?: string | null
  description?: string | null
  toc?: string | null
  covers: string[]
  sources: string[]
}
interface ResultLike<I extends InfoLike> {
  code?: string | null
  found: boolean
  error?: string | null
  info?: I | null
  statuses: { name: string; state: 'ok' | 'none' | 'err'; note?: string | null }[]
  links: { label: string; url: string }[]
  card?: unknown
}

const NAME = '誠品線上'

/** 回傳合併後的新結果（不改原本的物件） */
export function mergeEslite<I extends InfoLike, R extends ResultLike<I>>(result: R, isbn13: string, outcome: EsliteOutcome): R {
  // 誠品是第 1 順位：查詢來源、其他網站、資料來源都排第一個
  const statuses = [
    'error' in outcome
      ? { name: NAME, state: 'err' as const, note: outcome.error }
      : { name: NAME, state: outcome.book ? 'ok' as const : 'none' as const },
    ...result.statuses.filter(s => s.name !== NAME),
  ]
  if ('error' in outcome || !outcome.book || result.error || result.card) return { ...result, statuses }

  const e = outcome.book
  // 其他網站的「誠品線上」連結改成直接開商品頁（原本是搜尋頁）
  const links = [
    { label: NAME, url: e.url },
    ...result.links.filter(l => !l.label.includes('誠品')),
  ]
  const base = (result.info ?? { kind: 'book', code: isbn13, authors: [], categories: [], covers: [], sources: [] }) as I
  // ⭐ 2026-11-07 業主決定：誠品第 1 順位。誠品有值就用誠品的，誠品沒有的欄位才用其他來源（國圖、Google Books…）
  const info: I = {
    ...base,
    title: e.title || base.title,
    subtitle: e.subtitle || base.subtitle,
    authors: e.authors.length ? e.authors : base.authors,
    publisher: e.publisher || base.publisher,
    date: e.date || base.date,
    pages: e.pages || base.pages,
    price: e.listPrice ? `NT$${e.listPrice}` : base.price,
    categories: e.categories.length ? e.categories : base.categories,
    language: e.language || base.language,
    description: withAuthorIntro(e.description || base.description, e.authorIntro),
    toc: e.toc || base.toc,
    covers: e.cover ? [e.cover, ...base.covers.filter(c => c !== e.cover)] : base.covers,
    sources: [NAME, ...base.sources.filter(s => s !== NAME)],
  }
  return { ...result, found: true, info, statuses, links }
}

function withAuthorIntro(desc: string | null | undefined, intro?: string) {
  if (!intro || desc?.includes(intro.slice(0, 30))) return desc
  return desc ? `${desc}\n\n【作者介紹】\n${intro}` : `【作者介紹】\n${intro}`
}
