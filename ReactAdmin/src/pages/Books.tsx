import { useCallback, useEffect, useRef, useState } from 'react'
import {
  Card, Table, Button, Space, Input, Checkbox, message, Tag, Popconfirm, Modal, Typography, Descriptions, Alert, type InputRef,
} from 'antd'
import { DeleteOutlined, DownloadOutlined, EditOutlined, ReloadOutlined, SearchOutlined, LinkOutlined } from '@ant-design/icons'
import { api, ApiError } from '../api'
import GuideAlert from '../components/GuideAlert'

/**
 * 書單（Lobsang，2026-11-07）
 * ・上半部：用 ISBN／條碼查書（後端同時查國圖、Google Books、openBD…），查到後加入書單
 * ・下半部：書單列表，可搜尋、改備註、刪除、匯出 CSV
 * ⭐ 書單會出現在公開頁（主站 /）的「搜尋書單」；備註只在後台看得到。
 * ⭐ 移植自 C:\Han\book\isbn.html，查詢邏輯在後端 BookLookupService。
 */

interface BookInfo {
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
interface LookupResult {
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

function Cover({ covers, width }: { covers: string[]; width: number }) {
  const [i, setI] = useState(0)
  useEffect(() => setI(0), [covers])
  const src = covers[i]
  if (!src) return <div style={{ width, height: width * 1.35, background: 'var(--c-bg-subtle, #f1f5f9)', borderRadius: 4 }} />
  return (
    <img key={src} src={src} alt="" referrerPolicy="no-referrer" style={{ width, borderRadius: 4, display: 'block' }}
      onLoad={e => { if ((e.target as HTMLImageElement).naturalWidth < 10) setI(i + 1) }}
      onError={() => setI(i + 1)} />
  )
}

function infoItems(b: BookInfo) {
  const rows: [string, string | null | undefined][] = b.kind === 'product'
    ? [['品牌', b.brand], ['規格', b.quantity], ['分類', b.categories.join('、')], ['銷售地區', b.countries],
       ['條碼註冊地', b.gs1], ['條碼', b.code], ['資料來源', b.sources.join('、')]]
    : [['作者', b.authors.join('、')], ['出版社', b.publisher], ['出版日期', b.date], ['版次', b.edition],
       ['頁數', b.pages ? `${b.pages} 頁` : null], ['價格', b.price], ['語言', b.language],
       ['分類', b.categories.join('、')], ['ISBN-13', b.code], ['ISBN-10', b.isbn10],
       ['產地判斷', b.regionLabel], ['資料來源', b.sources.join('、')]]
  return rows.filter(r => r[1]).map(([k, v]) => ({ key: k, label: k, children: v }))
}

function BookDetail({ info }: { info: BookInfo }) {
  return (
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

export default function Books() {
  // ---------- 查書 ----------
  const [code, setCode] = useState('')
  const [looking, setLooking] = useState(false)
  const [lookup, setLookup] = useState<{ result: LookupResult; existingId: number | null; existingNote: string | null } | null>(null)
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)
  const [autoAdd, setAutoAdd] = useState(() => { try { return localStorage.getItem(AUTO_ADD_KEY) === '1' } catch { return false } })
  const inputRef = useRef<InputRef>(null)

  // ---------- 書單 ----------
  const [rows, setRows] = useState<BookRow[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [q, setQ] = useState('')
  const [loading, setLoading] = useState(false)
  const [editing, setEditing] = useState<BookRow | null>(null)
  const [editNote, setEditNote] = useState('')

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
      message.success(r.isNew ? '已加入書單' : '書單裡已有這本，已更新資料')
      setLookup(prev => prev ? { ...prev, existingId: r.id, existingNote: noteText } : prev)
      load(q, 1)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '儲存失敗')
    } finally { setSaving(false) }
  }, [load, q])

  const runLookup = async () => {
    if (!code.trim()) return
    setLooking(true); setLookup(null)
    try {
      const r = await api.get<{ result: LookupResult; existingId: number | null; existingNote: string | null }>(
        `/api/admin/books/lookup?code=${encodeURIComponent(code)}`)
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

  const remove = async (row: BookRow) => {
    try {
      await api.del(`/api/admin/books/${row.id}`)
      message.success('已從書單移除')
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

  const exportCsv = async () => {
    try {
      const r = await api.get<{ items: BookRow[] }>(`/api/admin/books?q=${encodeURIComponent(q)}&pageSize=0`)
      const head = ['ISBN／條碼', 'ISBN-10', '類型', '名稱', '副標題', '作者／品牌', '出版社', '出版日期', '版次', '頁數',
        '規格／容量', '價格', '語言', '分類', '產地／條碼註冊地', '資料來源', '簡介／說明', '目次', '成分', '圖片網址', '備註', '加入日期']
      const lines = r.items.map(b => {
        const i = b.info, isP = b.kind === 'product'
        // 條碼用 ="..." 包起來，避免 Excel 變成科學記號或吃掉開頭的 0
        return [`="${b.code}"`, i?.isbn10 ? `="${i.isbn10}"` : '', isP ? '商品' : '書', b.title, i?.subtitle,
          b.authors, isP ? '' : b.publisher, b.pubDate, i?.edition, i?.pages, i?.quantity, i?.price, i?.language,
          i?.categories?.join('、'), isP ? i?.gs1 : i?.regionLabel, i?.sources?.join('、'), i?.description, i?.toc,
          i?.ingredients, i?.covers?.[0], b.note, b.createdAt.slice(0, 10)]
      })
      const esc = (v: unknown) => { const s = v == null ? '' : String(v); return /[",\n\r]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s }
      const csv = '\uFEFF' + [head, ...lines].map(l => l.map(esc).join(',')).join('\r\n')
      const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
      const a = document.createElement('a')
      a.href = url; a.download = `書單_${new Date().toISOString().slice(0, 10)}.csv`
      document.body.append(a); a.click(); a.remove()
      setTimeout(() => URL.revokeObjectURL(url), 2000)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '匯出失敗')
    }
  }

  const res = lookup?.result
  const canAdd = !!res && res.found && !!res.info && (res.kind === 'book' || res.kind === 'product')

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <GuideAlert id="books-intro" style={{ marginBottom: 0 }} title="用 ISBN 或商品條碼查書，查到後加入書單">
        書單會公開在主站的「搜尋書單」，任何人都能搜尋；備註只有後台看得到。可以用條碼掃描器直接掃，
        勾「查到後自動加入」就能連續掃。台灣書會查國家圖書館，可能要等十幾秒。
      </GuideAlert>

      <Card title="Search" extra={<a href="/" target="_blank" rel="noreferrer"><LinkOutlined /> 公開 Search 頁</a>}>
        <Space.Compact style={{ width: '100%', maxWidth: 560 }}>
          <Input ref={inputRef} value={code} onChange={e => setCode(e.target.value)} onPressEnter={runLookup}
            placeholder="ISBN 或商品條碼，例如 9786264186421" inputMode="numeric" autoFocus allowClear />
          <Button type="primary" icon={<SearchOutlined />} loading={looking} onClick={runLookup}>查詢</Button>
        </Space.Compact>
        <div style={{ marginTop: 8 }}>
          <Checkbox checked={autoAdd} onChange={e => {
            setAutoAdd(e.target.checked)
            try { localStorage.setItem(AUTO_ADD_KEY, e.target.checked ? '1' : '0') } catch { /* 無痕模式等 */ }
          }}>查到後自動加入書單</Checkbox>
        </div>

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
                {lookup?.existingId && <Tag color="green" style={{ marginBottom: 8 }}>已在書單裡</Tag>}
                <BookDetail info={res.info} />
                <div style={{ marginTop: 8 }}><StatusTags statuses={res.statuses} /></div>
                <Space style={{ marginTop: 12, width: '100%', maxWidth: 560 }} direction="vertical">
                  <Input.TextArea value={note} onChange={e => setNote(e.target.value)} rows={2} maxLength={1000}
                    placeholder="備註（選填，只有後台看得到）：放在哪裡、借給誰…" />
                  <Button type="primary" loading={saving} onClick={() => save(res.info!, note)}>
                    {lookup?.existingId ? '更新書單裡的資料' : '加入書單'}
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

      <Card title={`書單（${total} 筆）`} extra={
        <Space>
          <Button icon={<DownloadOutlined />} onClick={exportCsv} disabled={!total}>匯出 CSV</Button>
          <Button icon={<ReloadOutlined />} onClick={() => load(q, page)} />
        </Space>
      }>
        <Input.Search value={q} onChange={e => setQ(e.target.value)} onSearch={v => load(v, 1)} allowClear
          placeholder="書名、作者、出版社或 ISBN" style={{ maxWidth: 360, marginBottom: 12 }} />
        <Table<BookRow>
          rowKey="id" size="small" loading={loading} dataSource={rows} scroll={{ x: 900 }}
          pagination={{ current: page, pageSize: 20, total, showSizeChanger: false, onChange: p => load(q, p) }}
          expandable={{ expandedRowRender: r => r.info ? <BookDetail info={r.info} /> : null, rowExpandable: r => !!r.info }}
          columns={[
            { title: '', key: 'cover', width: 52, render: (_, r) => <Cover covers={r.info?.covers ?? []} width={36} /> },
            { title: '類型', dataIndex: 'kind', width: 64, render: k => k === 'product' ? <Tag>商品</Tag> : <Tag color="blue">書</Tag> },
            { title: '名稱', dataIndex: 'title' },
            { title: '作者／品牌', dataIndex: 'authors', width: 160 },
            { title: '出版社／規格', dataIndex: 'publisher', width: 140 },
            { title: '出版日期', dataIndex: 'pubDate', width: 100 },
            { title: 'ISBN／條碼', dataIndex: 'code', width: 130 },
            { title: '備註', dataIndex: 'note', width: 160, ellipsis: true },
            {
              title: '操作', key: 'op', width: 110, fixed: 'right', render: (_, r) => (
                <Space size={4}>
                  <Button size="small" icon={<EditOutlined />} onClick={() => { setEditing(r); setEditNote(r.note ?? '') }}>備註</Button>
                  <Popconfirm title={`從書單移除「${r.title}」？`} okText="移除" cancelText="取消" onConfirm={() => remove(r)}>
                    <Button size="small" danger icon={<DeleteOutlined />} aria-label="移除" />
                  </Popconfirm>
                </Space>
              ),
            },
          ]}
        />
      </Card>

      <Modal title={editing ? `備註：${editing.title}` : ''} open={!!editing} onOk={saveNote} onCancel={() => setEditing(null)}
        okText="儲存" cancelText="取消" destroyOnHidden>
        <Input.TextArea value={editNote} onChange={e => setEditNote(e.target.value)} rows={4} maxLength={1000} showCount
          placeholder="只有後台看得到：放在哪裡、借給誰…" />
      </Modal>
    </Space>
  )
}
