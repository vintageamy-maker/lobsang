import { useCallback, useEffect, useRef, useState, type CSSProperties } from 'react'
import {
  Card, Table, Button, Space, Input, Checkbox, message, Tag, Popconfirm, Modal, Typography, Descriptions, Alert, Grid,
  Form, Radio, Select, DatePicker, Collapse, Tabs, InputNumber, type InputRef,
} from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { CameraOutlined, DeleteOutlined, DownloadOutlined, EditOutlined, ReloadOutlined, ScanOutlined, SearchOutlined, SendOutlined, LinkOutlined } from '@ant-design/icons'
import { api, ApiError } from '../api'
import GuideAlert from '../components/GuideAlert'
import { Link } from 'react-router-dom'
import { useTabParam } from '../useTabParam'
import PurchaseOrders from './PurchaseOrders'
import { BarcodeScanner, canScan } from '../components/BarcodeScanner'
import { esliteCode, fetchEslite, mergeEslite } from '../components/eslite'

/**
 * 採購清單（Lobsang，2026-11-07）
 * ・上半部：用 ISBN／條碼查書（後端同時查國圖、Google Books、openBD…），查到後加入採購清單
 * ・下半部：採購清單列表，可搜尋、改備註、刪除、匯出 CSV
 * ⭐ 採購清單會出現在公開頁（主站 /）的「搜尋採購清單」；備註只在後台看得到。
 * ⭐ 移植自 C:\Han\book\isbn.html，查詢邏輯在後端 BookLookupService。
 */

export interface BookInfo {
  kind: 'book' | 'product'
  code: string
  isbn10?: string | null
  regionLabel?: string | null
  title?: string | null
  subtitle?: string | null
  authors: string[]
  publisher?: string | null
  date?: string | null
  edition?: string | null
  pages?: number | null
  price?: string | null
  salePrice?: string | null   // 售價（目前只有誠品有）
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
interface SourceStatus { name: string; state: 'ok' | 'none' | 'err'; note?: string | null }
interface LinkItem { label: string; url: string }
export interface LookupResult {
  kind?: string | null
  code?: string | null
  notes: string[]
  error?: string | null
  found: boolean
  info?: BookInfo | null
  statuses: SourceStatus[]
  links: LinkItem[]
  card?: { title: string; message?: string | null; rows: [string, string][]; links: LinkItem[] } | null
}
interface BookRow {
  id: number
  code: string
  kind: string
  title: string
  authors: string | null
  publisher: string | null
  pubDate: string | null
  note: string | null
  createdAt: string
  info: BookInfo | null
}

const AUTO_ADD_KEY = 'lobsang_books_auto_add'

export type AdminLookup = { result: LookupResult; existingId: number | null; existingNote: string | null }

/**
 * 後台查書：伺服器查詢＋誠品線上（在瀏覽器查，見 components/eslite.ts）同時出發，兩邊都回來才合併。
 * ⭐ 合併後的 info 存進採購清單時會一起送到後端（DataJson），所以採購清單裡的書也帶有誠品的資料。
 */
export async function adminLookup(code: string): Promise<AdminLookup> {
  const isbn = esliteCode(code)   // ISBN 或商品條碼（誠品也賣商品）
  const [r, outcome] = await Promise.all([
    api.get<AdminLookup>(`/api/admin/books/lookup?code=${encodeURIComponent(code)}`),
    isbn ? fetchEslite(isbn) : Promise.resolve(null),
  ])
  return isbn && outcome ? { ...r, result: mergeEslite(r.result, isbn, outcome) } : r
}

// ---------- 連續掃描 ----------
// ⭐ 相機不關、掃到一個就丟進佇列在背景查詢＋加入採購清單，使用者不用等查完就能掃下一本
//    （台灣書查國圖要十幾秒，排隊一本本等會很慢，所以同時最多查 BATCH_PARALLEL 本）
type BatchStatus = 'pending' | 'added' | 'exists' | 'notfound' | 'error'
interface BatchItem { code: string; status: BatchStatus; title?: string; msg?: string }
const BATCH_PARALLEL = 3
const BATCH_LABEL: Record<BatchStatus, string> = {
  pending: '查詢中…', added: '已加入', exists: '本來就在採購清單', notfound: '查不到', error: '失敗',
}
const BATCH_COLOR: Record<BatchStatus, string> = {
  pending: 'processing', added: 'success', exists: 'default', notfound: 'error', error: 'error',
}

function batchCounts(items: BatchItem[]) {
  const n = (s: BatchStatus) => items.filter(i => i.status === s).length
  return { added: n('added'), exists: n('exists'), failed: n('notfound') + n('error'), pending: n('pending') }
}

/** 相機畫面下方的即時結果：總數＋最近 3 本 */
function BatchOverlay({ items }: { items: BatchItem[] }) {
  if (!items.length) return <div style={OV.box}>還沒掃到，對準書背的條碼</div>
  const c = batchCounts(items)
  return (
    <div style={OV.box}>
      <div style={{ fontWeight: 700, marginBottom: 4 }}>
        已掃 {items.length} 本：加入 {c.added}・原本就有 {c.exists}・查不到 {c.failed}{c.pending ? `・查詢中 ${c.pending}` : ''}
      </div>
      {items.slice(0, 3).map(i => (
        <div key={i.code} style={OV.row}>
          <span style={{ ...OV.status, color: OV_COLOR[i.status] }}>{BATCH_LABEL[i.status]}</span>
          <span style={OV.title}>{i.title ?? i.code}</span>
        </div>
      ))}
    </div>
  )
}
const OV_COLOR: Record<BatchStatus, string> = { pending: '#fcd34d', added: '#4ade80', exists: '#cbd5e1', notfound: '#f87171', error: '#f87171' }
const OV: Record<string, CSSProperties> = {
  box: {
    width: '100%', maxWidth: 420, padding: '10px 14px', borderRadius: 12, background: 'rgba(15,23,42,.78)',
    color: '#fff', fontSize: 14, lineHeight: 1.6,
  },
  row: { display: 'flex', gap: 8, minWidth: 0 },
  status: { flex: 'none', width: 96 },
  title: { minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
}

/** 伺服器回的時間是 UTC 但不帶 Z（後台各頁的慣例），補上再轉本地時間 */
function localDate(s: string) {
  return dayjs(/[zZ]|[+-]\d\d:?\d\d$/.test(s) ? s : s + 'Z')
}

interface ExportFilter {
  keyword: string
  kind: 'all' | 'book' | 'product'
  regions: string[]          // 產地判斷（台灣書、日本書…）；空＝全部
  dates: [Dayjs, Dayjs] | null // 加入日期（含頭尾兩天）
  note: 'all' | 'with' | 'without'
}
const EMPTY_FILTER: ExportFilter = { keyword: '', kind: 'all', regions: [], dates: null, note: 'all' }

/**
 * 匯出可選的欄位（順序＝檔案欄位順序）。common＝「常用」快速選取會勾的欄位；
 * code＝條碼類（CSV 用 ="..." 包、Excel 存成文字，避免變成科學記號或吃掉開頭的 0）；width＝Excel 欄寬（字元數）
 */
interface ExportColumn { key: string; label: string; common?: boolean; code?: boolean; width?: number; get: (b: BookRow) => unknown }
const EXPORT_COLUMNS: ExportColumn[] = [
  { key: 'code', label: 'ISBN／條碼', common: true, code: true, width: 16, get: b => b.code },
  { key: 'isbn10', label: 'ISBN-10', code: true, width: 13, get: b => b.info?.isbn10 },
  { key: 'kind', label: '類型', common: true, width: 7, get: b => (b.kind === 'product' ? '商品' : '書') },
  { key: 'title', label: '名稱', common: true, width: 36, get: b => b.title },
  { key: 'subtitle', label: '副標題', width: 28, get: b => b.info?.subtitle },
  { key: 'authors', label: '作者／品牌', common: true, width: 28, get: b => b.authors },
  { key: 'publisher', label: '出版社', common: true, width: 18, get: b => (b.kind === 'product' ? '' : b.publisher) },
  { key: 'pubDate', label: '出版日期', common: true, width: 11, get: b => b.pubDate },
  { key: 'edition', label: '版次', width: 12, get: b => b.info?.edition },
  { key: 'pages', label: '頁數', width: 7, get: b => b.info?.pages },
  { key: 'quantity', label: '規格／容量', width: 14, get: b => b.info?.quantity },
  { key: 'price', label: '定價', common: true, width: 12, get: b => b.info?.price },
  { key: 'language', label: '語言', width: 8, get: b => b.info?.language },
  { key: 'categories', label: '分類', width: 20, get: b => b.info?.categories?.join('、') },
  { key: 'region', label: '產地／條碼註冊地', width: 16, get: b => (b.kind === 'product' ? b.info?.gs1 : b.info?.regionLabel) },
  { key: 'sources', label: '資料來源', width: 24, get: b => b.info?.sources?.join('、') },
  { key: 'description', label: '簡介／說明', width: 50, get: b => b.info?.description },
  { key: 'toc', label: '目次', width: 40, get: b => b.info?.toc },
  { key: 'ingredients', label: '成分', width: 30, get: b => b.info?.ingredients },
  { key: 'cover', label: '圖片網址', width: 40, get: b => b.info?.covers?.[0] },
  { key: 'note', label: '備註', common: true, width: 30, get: b => b.note },
  { key: 'createdAt', label: '加入日期', common: true, width: 12, get: b => localDate(b.createdAt).format('YYYY-MM-DD') },
]
const ALL_COLS = EXPORT_COLUMNS.map(c => c.key)
const COMMON_COLS = EXPORT_COLUMNS.filter(c => c.common).map(c => c.key)
const EXPORT_COLS_KEY = 'lobsang_books_export_cols'
const EXPORT_FORMAT_KEY = 'lobsang_books_export_format'
export const EXPORT_FONT = '微軟正黑體'   // = Microsoft JhengHei
export const ROW_HEIGHT = 16             // 匯出 Excel 的列高（點），業主指定固定 16

export function downloadBlob(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url; a.download = name
  document.body.append(a); a.click(); a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 2000)
}

/** CSV 是純文字，存不了字型和顏色（Excel 開啟時一律用它的預設字型） */
function buildCsv(cols: ExportColumn[], items: BookRow[]): Blob {
  const esc = (v: unknown) => { const s = v == null ? '' : String(v); return /[",\n\r]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s }
  const cell = (c: ExportColumn, b: BookRow) => { const v = c.get(b); return c.code && v ? `="${v}"` : v }
  const csv = '﻿' + [cols.map(c => c.label), ...items.map(b => cols.map(c => cell(c, b)))]
    .map(l => l.map(esc).join(',')).join('\r\n')
  return new Blob([csv], { type: 'text/csv;charset=utf-8' })
}

/**
 * Excel：全部微軟正黑體；標題列黑底白字粗體置中、凍結在最上面；每列列高固定 16；條碼存成文字。
 * ⭐ exceljs 約 1 MB，按匯出才載入（import()），平常開採購清單頁不用下載。
 */
async function buildXlsx(cols: ExportColumn[], items: BookRow[]): Promise<Blob> {
  const ExcelJS = (await import('exceljs')).default
  const wb = new ExcelJS.Workbook()
  const ws = wb.addWorksheet('採購清單', { views: [{ state: 'frozen', ySplit: 1 }], properties: { defaultRowHeight: ROW_HEIGHT } })
  ws.columns = cols.map(c => ({ header: c.label, key: c.key, width: c.width ?? 14 }))
  for (const b of items) {
    ws.addRow(Object.fromEntries(cols.map(c => {
      const v = c.get(b)
      return [c.key, v == null || v === '' ? null : c.code ? String(v) : v]
    })))
  }
  ws.eachRow({ includeEmpty: true }, (row, n) => {
    row.eachCell({ includeEmpty: true }, (cell, colNo) => {
      if (n === 1) {
        cell.font = { name: EXPORT_FONT, bold: true, color: { argb: 'FFFFFFFF' } }
        cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF000000' } }
        cell.alignment = { vertical: 'middle', horizontal: 'center' }   // 標題置中（業主指定）
      } else {
        cell.font = { name: EXPORT_FONT }
        // ⚠ 列高固定，不自動換行（換行的話長文字只會露出一部分，反而難讀）
        cell.alignment = { vertical: 'middle' }
        if (cols[colNo - 1]?.code) cell.numFmt = '@'
      }
    })
  })
  ws.eachRow({ includeEmpty: true }, row => { row.height = ROW_HEIGHT })
  const buf = await wb.xlsx.writeBuffer()
  return new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
}
/** 上次勾的欄位（記在這台瀏覽器）；沒有或壞掉就用全部欄位（跟改版前一樣） */
function loadExportCols(): string[] {
  try {
    const v = JSON.parse(localStorage.getItem(EXPORT_COLS_KEY) ?? 'null')
    if (Array.isArray(v)) { const ok = ALL_COLS.filter(k => v.includes(k)); if (ok.length) return ok }
  } catch { /* 無痕模式等 */ }
  return ALL_COLS
}

function applyExportFilter(rows: BookRow[], f: ExportFilter): BookRow[] {
  // 關鍵字規則與採購清單搜尋（後端 BookQueries.Filter）一致：空白分開＝每個字都要符合；後台多比對備註
  const words = f.keyword.trim().toLowerCase().split(/\s+/).filter(Boolean)
  return rows.filter(b => {
    if (f.kind !== 'all' && b.kind !== f.kind) return false
    if (f.regions.length && !(b.kind !== 'product' && f.regions.includes(b.info?.regionLabel ?? ''))) return false
    if (f.note === 'with' && !b.note?.trim()) return false
    if (f.note === 'without' && b.note?.trim()) return false
    if (f.dates) {
      const d = localDate(b.createdAt)
      if (d.isBefore(f.dates[0].startOf('day')) || d.isAfter(f.dates[1].endOf('day'))) return false
    }
    if (words.length) {
      const hay = [b.title, b.authors, b.publisher, b.note].map(v => (v ?? '').toLowerCase())
      const code = b.code.toLowerCase()
      if (!words.every(w => code.includes(w.replace(/-/g, '')) || hay.some(h => h.includes(w)))) return false
    }
    return true
  })
}

/** fixed：固定高度（寬 × 1.35）並裁切，採購清單表格用，不然每本封面比例不同、列高會不一致 */
export function Cover({ covers, width, fixed }: { covers: string[]; width: number; fixed?: boolean }) {
  const [i, setI] = useState(0)
  useEffect(() => setI(0), [covers])
  const src = covers[i]
  if (!src) return <div style={{ width, height: width * 1.35, background: 'var(--c-bg-subtle, #f1f5f9)', borderRadius: 4 }} />
  return (
    <img key={src} src={src} alt="" referrerPolicy="no-referrer"
      style={{ width, borderRadius: 4, display: 'block', ...(fixed ? { height: width * 1.35, objectFit: 'cover' as const } : {}) }}
      onLoad={e => { if ((e.target as HTMLImageElement).naturalWidth < 10) setI(i + 1) }}
      onError={() => setI(i + 1)} />
  )
}

function infoItems(b: BookInfo) {
  const rows: [string, string | null | undefined][] = b.kind === 'product'
    ? [['品牌', b.brand], ['規格', b.quantity], ['定價', b.price], ['分類', b.categories.join('、')], ['銷售地區', b.countries],
       ['條碼註冊地', b.gs1], ['條碼', b.code], ['資料來源', b.sources.join('、')]]
    : [['作者', b.authors.join('、')], ['出版社', b.publisher], ['出版日期', b.date], ['版次', b.edition],
       ['頁數', b.pages ? `${b.pages} 頁` : null], ['定價', b.price], ['語言', b.language],
       ['分類', b.categories.join('、')], ['ISBN-13', b.code], ['ISBN-10', b.isbn10],
       ['產地判斷', b.regionLabel], ['資料來源', b.sources.join('、')]]
  return rows.filter(r => r[1]).map(([k, v]) => ({ key: k, label: k, children: v }))
}

export function BookDetail({ info }: { info: BookInfo }) {
  // 目次、成分可點開（2026-11-07 補上，跟公開頁一致）
  const more = [
    ...(info.ingredients ? [{ key: 'ing', label: '成分', children: <div style={{ whiteSpace: 'pre-wrap' }}>{info.ingredients}</div> }] : []),
    ...(info.toc ? [{ key: 'toc', label: '目次', children: <div style={{ whiteSpace: 'pre-wrap' }}>{info.toc.replace(/^目次\s*/, '')}</div> }] : []),
  ]
  return (
    <>
    <Space align="start" size={20} style={{ width: '100%' }} wrap>
      <Cover covers={info.covers} width={120} />
      <div style={{ flex: 1, minWidth: 260 }}>
        <Typography.Title level={4} style={{ marginTop: 0, marginBottom: 4 }}>{info.title || '（無名稱）'}</Typography.Title>
        {info.subtitle && <Typography.Text type="secondary">{info.subtitle}</Typography.Text>}
        <Descriptions size="small" column={1} style={{ marginTop: 8 }} items={infoItems(info)} />
        {info.description && (
          <Typography.Paragraph type="secondary" ellipsis={{ rows: 3, expandable: true, symbol: '展開' }} style={{ whiteSpace: 'pre-wrap', marginTop: 8 }}>
            {info.description}
          </Typography.Paragraph>
        )}
      </div>
    </Space>
    {more.length > 0 && <Collapse ghost size="small" style={{ marginTop: 8 }} items={more} />}
    </>
  )
}

function StatusTags({ statuses }: { statuses: SourceStatus[] }) {
  return (
    <Space size={[4, 4]} wrap>
      {statuses.map(s => (
        <Tag key={s.name} color={s.state === 'ok' ? 'green' : s.state === 'err' ? 'orange' : undefined}>
          {s.name}{s.state === 'ok' ? ' ✓' : s.state === 'none' ? ' 查無' : ` ⚠ ${s.note ?? ''}`}
        </Tag>
      ))}
    </Space>
  )
}

/**
 * 「採購候選」頁（選單 書籍 → 採購候選，2026-11-08）：兩個分頁
 *   ・採購清單（第一階段）：查書後加入的清單，可以勾選送審
 *   ・採購審核（第二階段）：採購單，主管核准／退回，核准的上架成商品（PurchaseOrders.tsx）
 */
export default function Books() {
  const [tab, setTab] = useTabParam('list', ['list', 'review'])
  const [reviewKey, setReviewKey] = useState(0)
  return (
    <Tabs activeKey={tab} onChange={setTab} destroyOnHidden={false} items={[
      {
        key: 'list', label: '採購清單',
        children: <PurchaseList onSubmitted={() => { setReviewKey(k => k + 1); setTab('review') }} />,
      },
      { key: 'review', label: '採購審核', children: <PurchaseOrders refreshKey={reviewKey} /> },
    ]} />
  )
}

function PurchaseList({ onSubmitted }: { onSubmitted: (orderNo: string) => void }) {
  // ---------- 查書 ----------
  const [code, setCode] = useState('')
  const [looking, setLooking] = useState(false)
  const [lookup, setLookup] = useState<{ result: LookupResult; existingId: number | null; existingNote: string | null } | null>(null)
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)
  const [autoAdd, setAutoAdd] = useState(() => { try { return localStorage.getItem(AUTO_ADD_KEY) === '1' } catch { return false } })
  const inputRef = useRef<InputRef>(null)
  const [scanning, setScanning] = useState(false)

  // ---------- 採購清單 ----------
  const [rows, setRows] = useState<BookRow[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [q, setQ] = useState('')
  const [loading, setLoading] = useState(false)
  const [editing, setEditing] = useState<BookRow | null>(null)
  const [editNote, setEditNote] = useState('')
  const [viewing, setViewing] = useState<BookRow | null>(null)

  // ---------- 送審（2026-11-08）：勾選 → 填數量 → 建採購單，書從採購清單移走 ----------
  const [picked, setPicked] = useState<BookRow[]>([])
  const [submitOpen, setSubmitOpen] = useState(false)
  const [qty, setQty] = useState<Record<number, number>>({})
  const [submitNote, setSubmitNote] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const openSubmit = () => {
    setQty(Object.fromEntries(picked.map(b => [b.id, qty[b.id] ?? 1])))
    setSubmitOpen(true)
  }
  const submitOrder = async () => {
    setSubmitting(true)
    try {
      const r = await api.post<{ id: number; orderNo: string }>('/api/admin/purchase-orders', {
        lines: picked.map(b => ({ bookId: b.id, quantity: qty[b.id] ?? 1 })), note: submitNote || null,
      })
      message.success(`已送審：採購單 ${r.orderNo}（${picked.length} 本已從採購清單移到採購單）`)
      setSubmitOpen(false); setPicked([]); setSubmitNote('')
      load(q, 1)
      onSubmitted(r.orderNo)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '送審失敗')
    } finally { setSubmitting(false) }
  }

  const load = useCallback(async (query: string, p: number) => {
    setLoading(true)
    try {
      const r = await api.get<{ total: number; items: BookRow[] }>(`/api/admin/books?q=${encodeURIComponent(query)}&page=${p}&pageSize=20`)
      setRows(r.items); setTotal(r.total); setPage(p)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '載入失敗')
    } finally { setLoading(false) }
  }, [])

  useEffect(() => { load('', 1) }, [load])

  const save = useCallback(async (info: BookInfo, noteText: string) => {
    setSaving(true)
    try {
      const r = await api.post<{ id: number; isNew: boolean }>('/api/admin/books', { info, note: noteText })
      message.success(r.isNew ? '已加入採購清單' : '採購清單裡已有這本，已更新資料')
      setLookup(prev => prev ? { ...prev, existingId: r.id, existingNote: noteText } : prev)
      load(q, 1)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '儲存失敗')
    } finally { setSaving(false) }
  }, [load, q])

  /** override：相機掃到的條碼（此時 code 這個 state 還沒更新，要直接帶值） */
  const runLookup = async (override?: string) => {
    const c = override ?? code
    if (!c.trim()) return
    setLooking(true); setLookup(null)
    try {
      const r = await adminLookup(c)
      setLookup(r)
      setNote(r.existingNote ?? '')
      const res = r.result
      if (autoAdd && res.found && res.info && (res.kind === 'book' || res.kind === 'product'))
        await save(res.info, r.existingNote ?? '')
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '查詢失敗')
    } finally {
      setLooking(false)
      inputRef.current?.select()
    }
  }

  // ---------- 連續掃描 ----------
  const [batchScanning, setBatchScanning] = useState(false)
  const [batch, setBatch] = useState<BatchItem[]>([])
  const batchSeen = useRef(new Set<string>())   // 這一輪掃過的條碼，重複的不再查
  const batchWaiting = useRef<string[]>([])
  const batchActive = useRef(0)
  const qRef = useRef(q)
  qRef.current = q

  const setBatchItem = (c: string, patch: Partial<BatchItem>) =>
    setBatch(list => list.map(i => i.code === c ? { ...i, ...patch } : i))

  const processBatch = async (c: string) => {
    try {
      const r = await adminLookup(c)
      const res = r.result
      if (!res.found || !res.info || (res.kind !== 'book' && res.kind !== 'product')) {
        setBatchItem(c, { status: 'notfound', msg: res.error ?? undefined })
        return
      }
      // 原本有備註就沿用，不要被清掉
      const s = await api.post<{ id: number; isNew: boolean }>('/api/admin/books', { info: res.info, note: r.existingNote ?? '' })
      setBatchItem(c, { status: s.isNew ? 'added' : 'exists', title: res.info.title ?? undefined })
    } catch (e) {
      setBatchItem(c, { status: 'error', msg: e instanceof ApiError ? e.message : '連不到伺服器' })
    }
  }

  const pumpBatch = () => {
    while (batchActive.current < BATCH_PARALLEL && batchWaiting.current.length) {
      const c = batchWaiting.current.shift()!
      batchActive.current++
      processBatch(c).finally(() => {
        batchActive.current--
        if (!batchActive.current && !batchWaiting.current.length) load(qRef.current, 1)   // 全部查完才重載採購清單
        pumpBatch()
      })
    }
  }

  const batchDetect = (c: string) => {
    if (batchSeen.current.has(c)) { message.info(`${c} 這輪已經掃過了`); return }
    batchSeen.current.add(c)
    setBatch(list => [{ code: c, status: 'pending' }, ...list])
    batchWaiting.current.push(c)
    pumpBatch()
  }

  const startBatch = () => {
    // 上一輪的結果還在畫面上就接著累計；按「清除」才重新開始
    setBatchScanning(true)
  }
  const clearBatch = () => {
    setBatch([])
    batchSeen.current = new Set()
  }

  const remove = async (row: BookRow) => {
    try {
      await api.del(`/api/admin/books/${row.id}`)
      message.success('已從採購清單移除')
      if (lookup?.existingId === row.id) setLookup({ ...lookup, existingId: null, existingNote: null })
      load(q, rows.length === 1 && page > 1 ? page - 1 : page)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '刪除失敗')
    }
  }

  const saveNote = async () => {
    if (!editing) return
    try {
      await api.put(`/api/admin/books/${editing.id}/note`, { note: editNote })
      message.success('備註已儲存')
      setEditing(null)
      load(q, page)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '儲存失敗')
    }
  }

  // ---------- 匯出（先篩選再匯出） ----------
  // ⭐ 篩選在前端做：採購清單量不大，一次抓全部（pageSize=0）再篩，篩選條件改了筆數馬上更新，不用每次問伺服器
  const [exportOpen, setExportOpen] = useState(false)
  const [exportAll, setExportAll] = useState<BookRow[] | null>(null)
  const [exportFilter, setExportFilter] = useState<ExportFilter>(EMPTY_FILTER)

  const openExport = async () => {
    setExportFilter({ ...EMPTY_FILTER, keyword: q })
    setExportAll(null)
    setExportOpen(true)
    try {
      const r = await api.get<{ items: BookRow[] }>('/api/admin/books?pageSize=0')
      setExportAll(r.items)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '讀取採購清單失敗')
      setExportOpen(false)
    }
  }
  const exportRows = exportAll ? applyExportFilter(exportAll, exportFilter) : []

  const [exportCols, setExportCols] = useState<string[]>(loadExportCols)
  const changeExportCols = (keys: string[]) => {
    // 保持欄位原本的順序（不照勾選的先後）
    const ordered = EXPORT_COLUMNS.map(c => c.key).filter(k => keys.includes(k))
    setExportCols(ordered)
    try { localStorage.setItem(EXPORT_COLS_KEY, JSON.stringify(ordered)) } catch { /* 無痕模式等 */ }
  }

  const [exportFormat, setExportFormat] = useState<'xlsx' | 'csv'>(() => {
    try { return localStorage.getItem(EXPORT_FORMAT_KEY) === 'csv' ? 'csv' : 'xlsx' } catch { return 'xlsx' }
  })
  const [exporting, setExporting] = useState(false)

  const exportFile = async (items: BookRow[]) => {
    const cols = EXPORT_COLUMNS.filter(c => exportCols.includes(c.key))
    const name = `採購清單_${dayjs().format('YYYY-MM-DD')}`
    setExporting(true)
    try {
      if (exportFormat === 'xlsx') downloadBlob(await buildXlsx(cols, items), `${name}.xlsx`)
      else downloadBlob(buildCsv(cols, items), `${name}.csv`)
      message.success(`已匯出 ${items.length} 筆`)
      setExportOpen(false)
    } catch {
      message.error('匯出失敗')
    } finally {
      setExporting(false)
    }
  }
  const regionOptions = [...new Set((exportAll ?? []).filter(b => b.kind !== 'product').map(b => b.info?.regionLabel).filter((v): v is string => !!v))]

  // 手機直式（< 768px）用精簡版採購清單
  const screens = Grid.useBreakpoint()
  const isNarrow = !screens.md
  const coverCell = (r: BookRow) => (
    <a onClick={() => setViewing(r)} aria-label={`看「${r.title}」的詳細資料`}><Cover covers={r.info?.covers ?? []} width={36} fixed /></a>
  )
  const opCell = (r: BookRow, compact: boolean) => (
    <Space size={4} direction={compact ? 'vertical' : 'horizontal'}>
      <Button size="small" icon={<EditOutlined />} aria-label="備註" onClick={() => { setEditing(r); setEditNote(r.note ?? '') }}>
        {compact ? null : '備註'}
      </Button>
      <Popconfirm title={`從採購清單移除「${r.title}」？`} okText="移除" cancelText="取消" onConfirm={() => remove(r)}>
        <Button size="small" danger icon={<DeleteOutlined />} aria-label="移除" />
      </Popconfirm>
    </Space>
  )

  const res = lookup?.result
  const canAdd = !!res && res.found && !!res.info && (res.kind === 'book' || res.kind === 'product')

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <GuideAlert id="books-intro" style={{ marginBottom: 0 }} title="用 ISBN 或商品條碼查書，查到後加入採購清單">
        採購清單只在後台，前台看不到（前台只有商品查詢）。可以按「掃描」用手機相機掃，或用條碼掃描器直接掃，
        一次要掃很多本就按「連續掃描」，相機不會關，掃到就自動加入採購清單。台灣書會查國家圖書館，可能要等十幾秒。
      </GuideAlert>
      {/* 2026-11-08 選單「書單」改名「採購候選」，頁面裡的字樣改叫「採購清單」；另有「商品」（實際販售） */}
      <Alert type="info" showIcon style={{ marginBottom: 0 }}
        message={<>這裡是「採購候選」（預計要採購的書）。實際販售的商品在選單「<Link to="/products">商品</Link>」。</>} />

      <Card extra={<a href="/" target="_blank" rel="noreferrer"><LinkOutlined /> 前台</a>}>
        <Space.Compact style={{ width: '100%', maxWidth: 560 }}>
          <Input ref={inputRef} value={code} onChange={e => setCode(e.target.value)} onPressEnter={() => runLookup()}
            placeholder="ISBN 或商品條碼，例如 9786264186421" inputMode="numeric" autoFocus allowClear />
          {canScan() && <Button icon={<CameraOutlined />} aria-label="用相機掃條碼" onClick={() => setScanning(true)}>{isNarrow ? null : '掃描'}</Button>}
          <Button type="primary" icon={<SearchOutlined />} loading={looking} onClick={() => runLookup()}>查詢</Button>
        </Space.Compact>
        {scanning && (
          <BarcodeScanner
            onClose={() => setScanning(false)}
            onDetect={c => { setScanning(false); setCode(c); runLookup(c) }}
          />
        )}
        {batchScanning && (
          <BarcodeScanner continuous onClose={() => setBatchScanning(false)} onDetect={batchDetect}>
            <BatchOverlay items={batch} />
          </BarcodeScanner>
        )}
        {/* ⚠ Space 是 inline-flex，外面一定要包 div，不然寬螢幕會跑到查詢鈕右邊擠在一起 */}
        <div style={{ marginTop: 8 }}><Space wrap>
          <Checkbox checked={autoAdd} onChange={e => {
            setAutoAdd(e.target.checked)
            try { localStorage.setItem(AUTO_ADD_KEY, e.target.checked ? '1' : '0') } catch { /* 無痕模式等 */ }
          }}>查到後自動加入採購清單</Checkbox>
          {canScan() && <Button size="small" icon={<ScanOutlined />} onClick={startBatch}>連續掃描（掃到就加入採購清單）</Button>}
        </Space></div>

        {batch.length > 0 && !batchScanning && (() => {
          const c = batchCounts(batch)
          return (
            <Alert style={{ marginTop: 12 }} type={c.failed ? 'warning' : 'success'} showIcon
              message={`連續掃描 ${batch.length} 本：加入 ${c.added}・原本就有 ${c.exists}・查不到或失敗 ${c.failed}${c.pending ? `・還在查 ${c.pending}` : ''}`}
              description={
                <div style={{ maxHeight: 240, overflowY: 'auto' }}>
                  {batch.map(i => (
                    <div key={i.code} style={{ display: 'flex', gap: 8, alignItems: 'baseline', padding: '2px 0' }}>
                      <Tag color={BATCH_COLOR[i.status]} style={{ flex: 'none' }}>{BATCH_LABEL[i.status]}</Tag>
                      {/* 查不到的點一下，會帶到上面的查詢框再查一次，看詳細原因 */}
                      <a onClick={() => { setCode(i.code); runLookup(i.code) }} style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {i.title ?? i.code}
                      </a>
                      {i.msg && <Typography.Text type="secondary" style={{ fontSize: 12, flex: 'none' }}>{i.msg}</Typography.Text>}
                    </div>
                  ))}
                </div>
              }
              action={
                <Space direction="vertical" size={4}>
                  <Button size="small" type="primary" onClick={startBatch}>繼續掃</Button>
                  <Button size="small" onClick={clearBatch} disabled={!!c.pending}>清除</Button>
                </Space>
              } />
          )
        })()}

        {res && (
          <div style={{ marginTop: 16 }}>
            {res.error && <Alert type="error" showIcon message={res.error} />}
            {res.notes.length > 0 && <Alert type="warning" style={{ marginBottom: 12 }} message={res.notes.join('；') + '。'} />}
            {res.card && (
              <Alert type="info" showIcon message={res.card.title}
                description={<>
                  {res.card.message && <div>{res.card.message}</div>}
                  {res.card.rows.map(([k, v]) => <div key={k}>{k}：{v}</div>)}
                </>} />
            )}
            {!res.error && !res.card && !res.found && (
              <Alert type="error" showIcon message={res.kind === 'product' ? `查不到這個商品（條碼 ${res.code}）` : `查不到這本書（ISBN ${res.code}）`}
                description={<StatusTags statuses={res.statuses} />} />
            )}
            {canAdd && res.info && (
              <>
                {lookup?.existingId && <Tag color="green" style={{ marginBottom: 8 }}>已在採購清單裡</Tag>}
                <BookDetail info={res.info} />
                <div style={{ marginTop: 8 }}><StatusTags statuses={res.statuses} /></div>
                <Space style={{ marginTop: 12, width: '100%', maxWidth: 560 }} direction="vertical">
                  <Input.TextArea value={note} onChange={e => setNote(e.target.value)} rows={2} maxLength={1000}
                    placeholder="備註（選填，只有後台看得到）：放在哪裡、借給誰…" />
                  <Button type="primary" loading={saving} onClick={() => save(res.info!, note)}>
                    {lookup?.existingId ? '更新採購清單裡的資料' : '加入採購清單'}
                  </Button>
                </Space>
              </>
            )}
            {res.links.length > 0 && (
              <div style={{ marginTop: 10, fontSize: 13 }}>
                其他網站：{res.links.map(l => <a key={l.url} href={l.url} target="_blank" rel="noopener noreferrer" style={{ marginRight: 10 }}>{l.label}</a>)}
              </div>
            )}
          </div>
        )}
      </Card>

      <Card title={`採購清單（${total} 筆）`} extra={
        <Space wrap>
          <Button type="primary" icon={<SendOutlined />} disabled={!picked.length} onClick={openSubmit}>送審（{picked.length}）</Button>
          <Button icon={<DownloadOutlined />} onClick={openExport} disabled={!total}>匯出</Button>
          <Button icon={<ReloadOutlined />} onClick={() => load(q, page)} />
        </Space>
      }>
        <Input.Search value={q} onChange={e => setQ(e.target.value)} onSearch={v => load(v, 1)} allowClear
          placeholder="書名、作者、出版社或 ISBN" style={{ maxWidth: 360, marginBottom: 12 }} />
        {/* ⭐ 手機（< md）改成「封面＋一欄文字＋操作」，不用左右滑動看寬表格 */}
        <Table<BookRow>
          rowKey="id" size="small" loading={loading} dataSource={rows} scroll={isNarrow ? undefined : { x: 1260 }}
          // 勾選要送審的書；換頁也記得之前勾的（preserveSelectedRowKeys）
          rowSelection={{
            selectedRowKeys: picked.map(b => b.id), preserveSelectedRowKeys: true,
            onChange: (_keys, selectedRows) => setPicked(selectedRows.filter(Boolean)),
          }}
          pagination={{ current: page, pageSize: 20, total, showSizeChanger: false, onChange: p => load(q, p), size: isNarrow ? 'small' : undefined }}
          columns={isNarrow ? [
            { title: '', key: 'cover', width: 52, render: (_, r) => coverCell(r) },
            {
              title: '採購清單', key: 'main', render: (_, r) => (
                <div style={{ minWidth: 0, lineHeight: 1.5 }}>
                  <a onClick={() => setViewing(r)} style={{ fontWeight: 600 }}>{r.title}</a>
                  {r.kind === 'product' && <Tag style={{ marginLeft: 6 }}>商品</Tag>}
                  {(r.authors || r.publisher || r.pubDate) && (
                    <div style={{ fontSize: 12, color: 'var(--c-text-secondary, #64748b)' }}>
                      {[r.authors, r.publisher, r.pubDate].filter(Boolean).join(' ・ ')}
                    </div>
                  )}
                  <div style={{ fontSize: 12 }}>
                    {r.info?.price && <Typography.Text strong style={{ marginRight: 8 }}>{r.info.price}</Typography.Text>}
                    <Typography.Text type="secondary" style={{ fontFamily: 'ui-monospace, Consolas, monospace' }}>{r.code}</Typography.Text>
                  </div>
                  {r.note && <Typography.Text type="secondary" ellipsis style={{ fontSize: 12, display: 'block' }}>備註：{r.note}</Typography.Text>}
                </div>
              ),
            },
            { title: '', key: 'op', width: 44, render: (_, r) => opCell(r, true) },
          ] : [
            { title: '', key: 'cover', width: 52, render: (_, r) => coverCell(r) },
            { title: '類型', dataIndex: 'kind', width: 64, render: k => k === 'product' ? <Tag>商品</Tag> : <Tag color="blue">書</Tag> },
            // ⭐ 文字欄一律單行、太長用「…」（滑過去看全文），每列高度才會一致
            { title: '名稱', dataIndex: 'title', width: 220, ellipsis: { showTitle: false }, render: (t: string, r) => <a onClick={() => setViewing(r)} title={t}>{t}</a> },
            { title: '作者／品牌', dataIndex: 'authors', width: 160, ellipsis: true },
            { title: '出版社／規格', dataIndex: 'publisher', width: 140, ellipsis: true },
            { title: '出版日期', dataIndex: 'pubDate', width: 100, ellipsis: true },
            { title: '定價', key: 'price', width: 110, ellipsis: { showTitle: false }, render: (_, r) => <span title={r.info?.price ?? ''}>{r.info?.price ?? ''}</span> },
            { title: 'ISBN／條碼', dataIndex: 'code', width: 140, ellipsis: true },
            { title: '備註', dataIndex: 'note', width: 160, ellipsis: true },
            { title: '操作', key: 'op', width: 110, fixed: 'right', render: (_, r) => opCell(r, false) },
          ]}
        />
      </Card>

      {/* 匯出前篩選（2026-11-07）：條件一改，下方筆數馬上更新；按「匯出」才下載 */}
      <Modal title="匯出採購清單" open={exportOpen} onCancel={() => setExportOpen(false)} destroyOnHidden width={520}
        footer={
          <Space>
            <Button onClick={() => setExportFilter(EMPTY_FILTER)}>清除條件</Button>
            <Button onClick={() => setExportOpen(false)}>取消</Button>
            <Button type="primary" icon={<DownloadOutlined />} loading={exporting}
              disabled={!exportAll || exportRows.length === 0 || exportCols.length === 0}
              onClick={() => exportFile(exportRows)}>匯出 {exportAll ? exportRows.length : ''} 筆</Button>
          </Space>
        }>
        <Form layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item label="關鍵字" extra="比對書名、作者、出版社、ISBN 和備註；空白分開＝每個字都要有">
            <Input value={exportFilter.keyword} allowClear placeholder="不填＝全部"
              onChange={e => setExportFilter({ ...exportFilter, keyword: e.target.value })} />
          </Form.Item>
          <Form.Item label="類型">
            <Radio.Group value={exportFilter.kind} optionType="button"
              onChange={e => setExportFilter({ ...exportFilter, kind: e.target.value })}
              options={[{ label: '全部', value: 'all' }, { label: '書', value: 'book' }, { label: '商品', value: 'product' }]} />
          </Form.Item>
          {regionOptions.length > 1 && exportFilter.kind !== 'product' && (
            <Form.Item label="產地（書）">
              <Select mode="multiple" allowClear placeholder="不選＝全部" value={exportFilter.regions}
                onChange={v => setExportFilter({ ...exportFilter, regions: v })}
                options={regionOptions.map(r => ({ label: r, value: r }))} />
            </Form.Item>
          )}
          <Form.Item label="加入採購清單的日期">
            <DatePicker.RangePicker style={{ width: '100%' }} value={exportFilter.dates} allowEmpty={[false, false]}
              placeholder={['開始日期', '結束日期']} inputReadOnly={isNarrow}
              onChange={v => setExportFilter({ ...exportFilter, dates: v && v[0] && v[1] ? [v[0], v[1]] : null })} />
          </Form.Item>
          <Form.Item label="備註" style={{ marginBottom: 8 }}>
            <Radio.Group value={exportFilter.note} optionType="button"
              onChange={e => setExportFilter({ ...exportFilter, note: e.target.value })}
              options={[{ label: '全部', value: 'all' }, { label: '有備註', value: 'with' }, { label: '沒有備註', value: 'without' }]} />
          </Form.Item>
          <Form.Item label="檔案格式" extra={exportFormat === 'xlsx' ? '微軟正黑體，標題列黑底白字' : 'CSV 是純文字，沒有字型和顏色'}>
            <Radio.Group value={exportFormat} optionType="button"
              onChange={e => { setExportFormat(e.target.value); try { localStorage.setItem(EXPORT_FORMAT_KEY, e.target.value) } catch { /* 無痕模式等 */ } }}
              options={[{ label: 'Excel（.xlsx）', value: 'xlsx' }, { label: 'CSV', value: 'csv' }]} />
          </Form.Item>
          <Form.Item style={{ marginBottom: 8 }}
            label={<Space size={8} wrap>
              <span>匯出欄位（{exportCols.length}／{ALL_COLS.length}）</span>
              <Button size="small" type="link" style={{ padding: 0 }} onClick={() => changeExportCols(ALL_COLS)}>全選</Button>
              <Button size="small" type="link" style={{ padding: 0 }} onClick={() => changeExportCols(COMMON_COLS)}>常用</Button>
              <Button size="small" type="link" style={{ padding: 0 }} onClick={() => changeExportCols([])}>全不選</Button>
            </Space>}
            extra="勾選的欄位會記在這台電腦，下次匯出沿用"
            validateStatus={exportCols.length === 0 ? 'error' : undefined}
            help={exportCols.length === 0 ? '至少要勾一個欄位' : undefined}>
            <Checkbox.Group value={exportCols} onChange={v => changeExportCols(v as string[])}
              style={{ display: 'grid', gridTemplateColumns: isNarrow ? '1fr 1fr' : '1fr 1fr 1fr', rowGap: 6 }}
              options={EXPORT_COLUMNS.map(c => ({ label: c.label, value: c.key }))} />
          </Form.Item>
        </Form>
        <Alert type={exportAll && exportRows.length === 0 ? 'warning' : 'info'} showIcon
          message={!exportAll ? '讀取採購清單中…'
            : exportRows.length === 0 ? `沒有符合條件的資料（採購清單共 ${exportAll.length} 筆）`
            : `符合條件 ${exportRows.length} 筆（採購清單共 ${exportAll.length} 筆）`} />
      </Modal>

      {/* 採購清單的詳細資料：點封面或書名開啟（2026-11-07 由展開列改成彈窗） */}
      <Modal title="詳細資料" open={!!viewing} onCancel={() => setViewing(null)} width={720} destroyOnHidden
        footer={viewing && (
          <Space>
            <Button icon={<EditOutlined />} onClick={() => { setEditing(viewing); setEditNote(viewing.note ?? ''); setViewing(null) }}>備註</Button>
            <Button type="primary" onClick={() => setViewing(null)}>關閉</Button>
          </Space>
        )}>
        {viewing && (
          <>
            {viewing.info
              ? <BookDetail info={viewing.info} />
              : <Typography.Title level={4} style={{ marginTop: 0 }}>{viewing.title}</Typography.Title>}
            <Descriptions size="small" column={1} style={{ marginTop: 12 }} items={[
              { key: 'note', label: '備註', children: viewing.note || <Typography.Text type="secondary">（無）</Typography.Text> },
              { key: 'created', label: '加入採購清單', children: new Date(viewing.createdAt + (/[zZ]|[+-]\d\d:?\d\d$/.test(viewing.createdAt) ? '' : 'Z')).toLocaleString('zh-TW', { hour12: false }) },
            ]} />
          </>
        )}
      </Modal>

      <Modal title={editing ? `備註：${editing.title}` : ''} open={!!editing} onOk={saveNote} onCancel={() => setEditing(null)}
        okText="儲存" cancelText="取消" destroyOnHidden>
        <Input.TextArea value={editNote} onChange={e => setEditNote(e.target.value)} rows={4} maxLength={1000} showCount
          placeholder="只有後台看得到：放在哪裡、借給誰…" />
      </Modal>

      {/* 送審：每本填採購數量 → 建採購單 */}
      <Modal title={`送審：${picked.length} 本`} open={submitOpen} onCancel={() => setSubmitOpen(false)} width={760} destroyOnHidden
        okText="送出採購單" cancelText="取消" onOk={submitOrder} okButtonProps={{ loading: submitting, disabled: !picked.length }}>
        <Alert type="info" showIcon style={{ marginBottom: 12 }}
          message="送出後這些書會從「採購清單」移到一張新的採購單，等主管在「採購審核」核准；退回的會回到採購清單。" />
        <Table<BookRow> rowKey="id" size="small" dataSource={picked} pagination={false} scroll={{ y: 360 }}
          columns={[
            { title: '書名', dataIndex: 'title', ellipsis: true },
            { title: 'ISBN／條碼', dataIndex: 'code', width: 140 },
            { title: '定價', key: 'price', width: 100, render: (_, r) => r.info?.price ?? '' },
            {
              title: '採購數量', key: 'qty', width: 110, render: (_, r) => (
                <InputNumber min={1} max={9999} precision={0} value={qty[r.id] ?? 1} style={{ width: 90 }}
                  onChange={v => setQty(m => ({ ...m, [r.id]: v ?? 1 }))} />
              ),
            },
          ]} />
        <Input.TextArea value={submitNote} onChange={e => setSubmitNote(e.target.value)} rows={2} maxLength={1000}
          placeholder="說明（選填）：為什麼要買、預算…" style={{ marginTop: 12 }} />
      </Modal>
    </Space>
  )
}
