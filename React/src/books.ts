// 查書 API 的型別與呼叫（對應後端 PublicBooksController／BookLookupService）

export interface BookInfo {
  kind: 'book' | 'product'
  code: string
  isbn10?: string | null
  region?: string | null
  regionLabel?: string | null
  title?: string | null
  subtitle?: string | null
  authors: string[]
  publisher?: string | null
  date?: string | null
  edition?: string | null
  pages?: number | null
  price?: string | null
  categories: string[]
  language?: string | null
  description?: string | null
  toc?: string | null
  covers: string[]
  sources: string[]
  brand?: string | null
  quantity?: string | null
  countries?: string | null
  ingredients?: string | null
  gs1?: string | null
}

export interface SourceStatus { name: string; state: 'ok' | 'none' | 'err'; note?: string | null }
export interface LinkItem { label: string; url: string }
export interface InfoCard { title: string; message?: string | null; rows: [string, string][]; links: LinkItem[] }

export interface LookupResult {
  kind?: string | null
  code?: string | null
  notes: string[]
  error?: string | null
  found: boolean
  info?: BookInfo | null
  statuses: SourceStatus[]
  links: LinkItem[]
  card?: InfoCard | null
}

export interface CatalogItem {
  code: string
  kind: string
  title: string
  authors?: string | null
  publisher?: string | null
  pubDate?: string | null
  info?: BookInfo | null
}

export class HttpError extends Error {
  constructor(public status: number, message: string) { super(message) }
}

async function getJson<T>(url: string): Promise<T> {
  const r = await fetch(url)
  if (r.status === 429) throw new HttpError(429, '查詢太頻繁了，請等一分鐘再試。')
  // 本機 Vite 轉發連不到 API 時回 500，正式站 nginx 則是 502／503／504：都代表「API 沒開或連不上」
  if ([500, 502, 503, 504].includes(r.status) && !(r.headers.get('content-type') ?? '').includes('json'))
    throw new HttpError(r.status, '查詢服務暫時連不上（伺服器可能沒有啟動），請稍後再試。')
  if (!r.ok) throw new HttpError(r.status, `伺服器錯誤（HTTP ${r.status}），請稍後再試。`)
  return r.json() as Promise<T>
}

export const lookupCode = (code: string) =>
  getJson<{ result: LookupResult; inList: boolean }>(`/api/public/books/lookup?code=${encodeURIComponent(code)}`)

export const searchCatalog = (q: string, page: number) =>
  getJson<{ total: number; page: number; pageSize: number; items: CatalogItem[] }>(
    `/api/public/books?q=${encodeURIComponent(q)}&page=${page}`)
