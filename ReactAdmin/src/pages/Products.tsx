/*
 * 商品（實際販售，2026-11-08 正式版）
 *
 * 跟「採購候選」（/books，預計要採購的書）分開：這裡是業主實際在賣、有自訂店內碼的東西。
 *   ・資料在資料庫 products 表（後端 AdminProductsController）；前台只查得到「上架」的（PublicProductsController）
 *   ・初始資料是 2026-11-08 照片清單 40 筆，寫在 migration AddProducts 裡（每個資料庫只放一次）
 *   ・商品名稱一律用業主清單上的名稱（業主決定）；「查書補資料」只補封面、作者、定價、簡介等，存在 DataJson
 *   ・Excel 匯入：前端讀檔 → 先預覽（新增／更新／有問題）→ 確認後整批送後端，以店內碼新增或更新
 * 選單「書籍 → 商品」由 DbSeeder 建立；權限先沿用 book.manage。
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  Alert, Button, Card, Collapse, Descriptions, Form, Input, InputNumber, Modal, Select, Space, Switch, Table, Tag,
  Typography, Upload, message, Grid,
} from 'antd'
import { CloudDownloadOutlined, DeleteOutlined, EditOutlined, ReloadOutlined, SearchOutlined, UploadOutlined } from '@ant-design/icons'
import { api, ApiError } from '../api'
import { adminLookup, Cover, downloadBlob, EXPORT_FONT, ROW_HEIGHT, type BookInfo } from './Books'

interface Product {
  id: number
  sku: string                 // 自訂店內碼（唯一）
  name: string                // 商品名稱（業主清單上的）
  code: string                // 國際條碼（ISBN／EAN）
  midCategory: string | null  // 中分類，例「1 立體書與紙藝」
  subCategory: string | null  // 子分類，例「1101 紙藝手作與技術理論」
  salePrice: number | null    // 自己的售價
  stock: number               // 庫存
  isListed: boolean           // 上架（前台只查得到上架的）
  info: BookInfo | null       // 查書補到的資料
}

// ---------- 匯入 ----------

/** Excel 標題對到哪個欄位（照業主清單的標題，也接受常見寫法） */
const HEADER_MAP: [keyof ImportRow, RegExp][] = [
  ['sku', /店內碼|sku/i],
  ['name', /商品名稱|名稱|書名/],
  ['code', /國際條碼|條碼|isbn|ean/i],
  ['mid', /中分類/],
  ['sub', /子分類/],
  ['salePrice', /售價/],
  ['stock', /庫存/],
]
interface ImportRow { sku: string; name: string; code: string; mid: string; sub: string; salePrice: string; stock: string }
interface PreviewRow extends ImportRow { line: number; status: 'new' | 'update' | 'error'; problem?: string }

const isEanOk = (s: string) => {
  if (!/^\d{8}$|^\d{12,13}$/.test(s)) return false
  const d = [...s.padStart(13, '0')].map(Number)
  return (d.slice(0, 12).reduce((a, x, i) => a + x * (i % 2 ? 3 : 1), 0) + d[12]) % 10 === 0
}
const numOrNull = (s: string) => (s.trim() === '' || !Number.isFinite(Number(s)) ? null : Math.round(Number(s)))

async function readSheet(file: File): Promise<string[][]> {
  if (/\.csv$/i.test(file.name)) {
    const text = (await file.text()).replace(/^﻿/, '')
    return text.split(/\r?\n/).filter(l => l.trim()).map(l => l.split(',').map(c => c.replace(/^="?|"$/g, '').replace(/^"|"$/g, '').trim()))
  }
  const ExcelJS = (await import('exceljs')).default
  const wb = new ExcelJS.Workbook()
  await wb.xlsx.load(await file.arrayBuffer())
  const ws = wb.worksheets[0]
  const rows: string[][] = []
  ws?.eachRow({ includeEmpty: false }, row => {
    const vals = (row.values as unknown[]).slice(1).map(v => {
      if (v == null) return ''
      if (typeof v === 'object' && v && 'text' in v) return String((v as { text: unknown }).text)
      if (typeof v === 'object' && v && 'result' in v) return String((v as { result: unknown }).result ?? '')
      return String(v)
    })
    rows.push(vals.map(s => s.trim()))
  })
  return rows
}

function toPreview(rows: string[][], existing: Product[]): PreviewRow[] | string {
  const headerIdx = rows.findIndex(r => r.some(c => /店內碼|國際條碼/.test(c)))
  if (headerIdx < 0) return '找不到標題列：至少要有「自訂店內碼」和「國際條碼」兩欄'
  const header = rows[headerIdx]
  const col: Partial<Record<keyof ImportRow, number>> = {}
  for (const [key, re] of HEADER_MAP) {
    const i = header.findIndex(h => re.test(h))
    if (i >= 0) col[key] = i
  }
  const seen = new Set<string>()
  return rows.slice(headerIdx + 1).map((r, i) => {
    const get = (k: keyof ImportRow) => (col[k] == null ? '' : r[col[k]!] ?? '')
    const row: ImportRow = {
      sku: get('sku'), name: get('name'), code: get('code').replace(/[\s-]/g, ''),
      mid: get('mid'), sub: get('sub'), salePrice: get('salePrice'), stock: get('stock'),
    }
    const line = headerIdx + i + 2
    let problem: string | undefined
    if (!row.sku) problem = '沒有店內碼'
    else if (seen.has(row.sku)) problem = '店內碼在檔案裡重複'
    else if (!row.name) problem = '沒有商品名稱'
    else if (row.code && !isEanOk(row.code)) problem = '條碼檢查碼不對'
    else if ((numOrNull(row.salePrice) ?? 0) < 0 || (numOrNull(row.stock) ?? 0) < 0) problem = '售價或庫存是負數'
    seen.add(row.sku)
    const status: PreviewRow['status'] = problem ? 'error' : existing.some(p => p.sku === row.sku) ? 'update' : 'new'
    return { ...row, line, status, problem }
  }).filter(r => r.sku || r.name || r.code)
}

async function buildSheet(items: Product[]): Promise<Blob> {
  const ExcelJS = (await import('exceljs')).default
  const wb = new ExcelJS.Workbook()
  const ws = wb.addWorksheet('商品', { views: [{ state: 'frozen', ySplit: 1 }], properties: { defaultRowHeight: ROW_HEIGHT } })
  ws.columns = [
    { header: '自訂店內碼', width: 14 }, { header: '商品名稱', width: 48 }, { header: '國際條碼', width: 16 },
    { header: '商品中分類', width: 16 }, { header: '商品子分類', width: 26 }, { header: '售價', width: 8 }, { header: '庫存', width: 8 },
    { header: '上架', width: 6 }, { header: '定價（查書）', width: 14 },
  ]
  for (const p of items) {
    ws.addRow([p.sku, p.name, p.code, p.midCategory ?? '', p.subCategory ?? '', p.salePrice ?? '', p.stock, p.isListed ? '是' : '否', p.info?.price ?? ''])
  }
  ws.eachRow({ includeEmpty: true }, (row, n) => {
    row.height = ROW_HEIGHT
    row.eachCell({ includeEmpty: true }, c => {
      c.font = n === 1 ? { name: EXPORT_FONT, bold: true, color: { argb: 'FFFFFFFF' } } : { name: EXPORT_FONT }
      if (n === 1) {
        c.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF000000' } }
        c.alignment = { vertical: 'middle', horizontal: 'center' }
      } else c.alignment = { vertical: 'middle' }
    })
    if (n > 1) { row.getCell(1).numFmt = '@'; row.getCell(3).numFmt = '@' }
  })
  return new Blob([await wb.xlsx.writeBuffer()], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
}

// ---------- 頁面 ----------

export default function Products() {
  const [items, setItems] = useState<Product[]>([])
  const [loading, setLoading] = useState(false)
  const [subs, setSubs] = useState<string[]>([])
  const [mids, setMids] = useState<string[]>([])

  // ⭐ 商品量不大：一次抓全部（pageSize=0），搜尋和篩選在前端做，改條件馬上有結果
  const load = useCallback(async () => {
    setLoading(true)
    try {
      const r = await api.get<{ items: Product[]; subs: string[]; mids: string[] }>('/api/admin/products?pageSize=0')
      setItems(r.items); setSubs(r.subs); setMids(r.mids)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '載入商品失敗')
    } finally { setLoading(false) }
  }, [])
  useEffect(() => { load() }, [load])

  const [q, setQ] = useState('')
  const [subFilter, setSubFilter] = useState<string | undefined>()
  const [listedFilter, setListedFilter] = useState<'all' | 'on' | 'off'>('all')
  const [viewingId, setViewingId] = useState<number | null>(null)
  const [editing, setEditing] = useState<Product | null>(null)

  // ---------- 刪除（2026-11-08 業主：真的刪掉、要先確認；可勾選多筆）----------
  const [picked, setPicked] = useState<number[]>([])
  const remove = async (ids: number[]) => {
    try {
      const r = await api.post<{ count: number }>('/api/admin/products/delete', { ids })
      message.success(`已刪除 ${r.count} 筆商品`)
      setPicked(list => list.filter(id => !ids.includes(id)))
      setViewingId(null)
      load()
    } catch (e) { message.error(e instanceof ApiError ? e.message : '刪除失敗') }
  }
  const confirmRemove = (ids: number[]) => {
    const names = items.filter(p => ids.includes(p.id)).map(p => `${p.sku} ${p.name}`)
    Modal.confirm({
      title: `確定要刪除 ${ids.length} 筆商品？`,
      content: (
        <div>
          <div style={{ maxHeight: 200, overflowY: 'auto', margin: '8px 0' }}>{names.map(n => <div key={n}>・{n}</div>)}</div>
          <Typography.Text type="danger">刪除後無法復原，前台也查不到。</Typography.Text>
        </div>
      ),
      okText: '刪除', okButtonProps: { danger: true }, cancelText: '取消',
      onOk: () => remove(ids),
    })
  }
  const [form] = Form.useForm()
  const isNarrow = !Grid.useBreakpoint().md
  const viewing = items.find(p => p.id === viewingId) ?? null

  const shown = useMemo(() => items.filter(p => {
    const kw = q.trim().toLowerCase()
    if (kw && ![p.sku, p.name, p.code, p.info?.authors.join(' ') ?? ''].some(v => v.toLowerCase().includes(kw))) return false
    if (subFilter && p.subCategory !== subFilter) return false
    if (listedFilter === 'on' && !p.isListed) return false
    if (listedFilter === 'off' && p.isListed) return false
    return true
  }), [items, q, subFilter, listedFilter])

  const replace = (p: Product) => setItems(list => list.map(x => (x.id === p.id ? p : x)))

  const save = async (p: Product, patch: Partial<Product>) => {
    const next = { ...p, ...patch }
    try {
      const r = await api.put<Product>(`/api/admin/products/${p.id}`, {
        name: next.name, midCategory: next.midCategory, subCategory: next.subCategory,
        salePrice: next.salePrice, stock: next.stock, isListed: next.isListed,
      })
      replace(r)
      return true
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '儲存失敗')
      return false
    }
  }

  // ---------- 查書補資料（伺服器查書＋瀏覽器查誠品，合併後存進商品）----------
  const [filling, setFilling] = useState<{ done: number; total: number } | null>(null)
  const fillOne = async (p: Product) => {
    if (!p.code) return false
    try {
      const r = await adminLookup(p.code)
      if (!r.result.found || !r.result.info) return false
      replace(await api.put<Product>(`/api/admin/products/${p.id}/info`, r.result.info))
      return true
    } catch { return false }
  }
  // ⭐ 打開詳細資料時，還沒查過書的自動查一次（誠品優先），業主不用另外按（2026-11-08）
  const [autoFilling, setAutoFilling] = useState<number | null>(null)
  const autoTried = useRef(new Set<number>())
  useEffect(() => {
    const p = items.find(x => x.id === viewingId)
    if (!p || p.info || !p.code || autoTried.current.has(p.id)) return
    autoTried.current.add(p.id)   // 查不到的不要每次打開都再查
    setAutoFilling(p.id)
    fillOne(p).finally(() => setAutoFilling(null))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [viewingId])

  const fillAll = async () => {
    const todo = items.filter(p => p.code && !p.info)
    if (!todo.length) { message.info('全部都已經有查書資料了'); return }
    setFilling({ done: 0, total: todo.length })
    let done = 0, found = 0
    const queue = [...todo]
    // 同時查 3 本（跟連續掃描一樣），台灣書查國圖比較慢
    await Promise.all(Array.from({ length: 3 }, async () => {
      while (queue.length) {
        if (await fillOne(queue.shift()!)) found++
        setFilling({ done: ++done, total: todo.length })
      }
    }))
    setFilling(null)
    message.success(`查書補資料完成：${todo.length} 筆中查到 ${found} 筆`)
  }

  // ---------- 匯入 ----------
  const [preview, setPreview] = useState<PreviewRow[] | null>(null)
  const [importing, setImporting] = useState(false)
  const pickFile = async (file: File) => {
    try {
      const rows = await readSheet(file)
      const p = toPreview(rows, items)
      if (typeof p === 'string') message.error(p)
      else if (!p.length) message.warning('檔案裡沒有資料')
      else setPreview(p)
    } catch {
      message.error('讀不到這個檔案，請用 Excel（.xlsx）或 CSV')
    }
    return false   // 不要讓 antd 自己上傳
  }
  const confirmImport = async () => {
    if (!preview) return
    const ok = preview.filter(r => r.status !== 'error')
    setImporting(true)
    try {
      const r = await api.post<{ added: number; updated: number; errors: { sku: string; message: string }[] }>('/api/admin/products/import', {
        rows: ok.map(x => ({
          sku: x.sku, name: x.name, code: x.code, midCategory: x.mid || null, subCategory: x.sub || null,
          salePrice: numOrNull(x.salePrice), stock: numOrNull(x.stock),
        })),
      })
      message.success(`已匯入：新增 ${r.added}、更新 ${r.updated}${r.errors.length ? `、伺服器擋下 ${r.errors.length}` : ''}`)
      setPreview(null)
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '匯入失敗')
    } finally { setImporting(false) }
  }

  const openEdit = (p: Product) => {
    setEditing(p)
    form.setFieldsValue({ name: p.name, midCategory: p.midCategory, subCategory: p.subCategory, salePrice: p.salePrice, stock: p.stock, isListed: p.isListed })
  }
  const saveEdit = async () => {
    if (!editing) return
    const v = await form.validateFields()
    const ok = await save(editing, {
      name: v.name, midCategory: v.midCategory ?? null, subCategory: v.subCategory ?? null,
      salePrice: v.salePrice ?? null, stock: v.stock ?? 0, isListed: !!v.isListed,
    })
    if (ok) { setEditing(null); message.success('已儲存') }
  }

  const money = (n: number | null) => (n == null ? '' : `NT$${n.toLocaleString()}`)
  const coverCell = (p: Product) => <a onClick={() => setViewingId(p.id)}><Cover covers={p.info?.covers ?? []} width={36} fixed /></a>

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <Card title={`商品（${items.length} 筆）`} extra={
        <Space wrap>
          <Upload accept=".xlsx,.csv" showUploadList={false} beforeUpload={pickFile}>
            <Button icon={<UploadOutlined />}>匯入 Excel</Button>
          </Upload>
          <Button icon={<CloudDownloadOutlined />} disabled={!items.length}
            onClick={async () => downloadBlob(await buildSheet(shown), `商品_${new Date().toISOString().slice(0, 10)}.xlsx`)}>
            匯出
          </Button>
          <Button icon={<SearchOutlined />} loading={!!filling} onClick={fillAll}>
            {filling ? `查書中 ${filling.done}/${filling.total}` : '查書補資料'}
          </Button>
          <Button danger icon={<DeleteOutlined />} disabled={!picked.length} onClick={() => confirmRemove(picked)}>刪除所選（{picked.length}）</Button>
          <Button icon={<ReloadOutlined />} onClick={load} aria-label="重新整理" />
        </Space>
      }>
        <Space wrap style={{ marginBottom: 12 }}>
          <Input.Search value={q} onChange={e => setQ(e.target.value)} allowClear placeholder="店內碼、商品名稱、條碼、作者" style={{ width: 260 }} />
          <Select allowClear placeholder="子分類（全部）" value={subFilter} onChange={setSubFilter} style={{ width: 240 }}
            options={subs.map(s => ({ value: s, label: s }))} />
          <Select value={listedFilter} onChange={setListedFilter} style={{ width: 120 }}
            options={[{ value: 'all', label: '上架＋下架' }, { value: 'on', label: '只看上架' }, { value: 'off', label: '只看下架' }]} />
          <Typography.Text type="secondary">符合 {shown.length} 筆（匯出也只匯出符合的）</Typography.Text>
        </Space>

        <Table<Product>
          rowKey="id" size="small" loading={loading} dataSource={shown} scroll={isNarrow ? undefined : { x: 1230 }}
          rowSelection={{ selectedRowKeys: picked, preserveSelectedRowKeys: true, onChange: keys => setPicked(keys as number[]) }}
          pagination={{ pageSize: 20, showSizeChanger: false }}
          columns={isNarrow ? [
            { title: '', key: 'cover', width: 52, render: (_, p) => coverCell(p) },
            {
              title: '商品', key: 'main', render: (_, p) => (
                <div style={{ lineHeight: 1.5 }}>
                  <a onClick={() => setViewingId(p.id)} style={{ fontWeight: 600 }}>{p.name}</a>
                  <div style={{ fontSize: 12 }}><Typography.Text type="secondary">{p.sku}・{p.subCategory}</Typography.Text></div>
                  <div style={{ fontSize: 12 }}>
                    {p.salePrice != null && <Typography.Text strong style={{ marginRight: 8 }}>{money(p.salePrice)}</Typography.Text>}
                    庫存 {p.stock}　{p.isListed ? <Tag color="green">上架</Tag> : <Tag>下架</Tag>}
                  </div>
                </div>
              ),
            },
            {
              title: '', key: 'op', width: 44, render: (_, p) => (
                <Space direction="vertical" size={4}>
                  <Button size="small" icon={<EditOutlined />} aria-label="編輯" onClick={() => openEdit(p)} />
                  <Button size="small" danger icon={<DeleteOutlined />} aria-label="刪除" onClick={() => confirmRemove([p.id])} />
                </Space>
              ),
            },
          ] : [
            { title: '', key: 'cover', width: 52, render: (_, p) => coverCell(p) },
            { title: '店內碼', dataIndex: 'sku', width: 110, sorter: (a, b) => a.sku.localeCompare(b.sku), defaultSortOrder: 'ascend' },
            { title: '商品名稱', dataIndex: 'name', width: 240, ellipsis: { showTitle: false }, render: (t: string, p) => <a onClick={() => setViewingId(p.id)} title={t}>{t}</a> },
            { title: '子分類', dataIndex: 'subCategory', width: 180, ellipsis: true },
            { title: '條碼', dataIndex: 'code', width: 130 },
            { title: '定價', key: 'price', width: 100, ellipsis: true, render: (_, p) => p.info?.price ?? '' },
            { title: '售價', dataIndex: 'salePrice', width: 90, render: (v: number | null) => money(v) },
            { title: '庫存', dataIndex: 'stock', width: 70, sorter: (a, b) => a.stock - b.stock },
            {
              title: '上架', dataIndex: 'isListed', width: 70,
              render: (v: boolean, p) => <Switch size="small" checked={v} onChange={c => save(p, { isListed: c })} />,
            },
            {
              title: '操作', key: 'op', width: 130, fixed: 'right', render: (_, p) => (
                <Space size={4}>
                  <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(p)}>編輯</Button>
                  <Button size="small" danger icon={<DeleteOutlined />} aria-label="刪除" onClick={() => confirmRemove([p.id])} />
                </Space>
              ),
            },
          ]}
        />
      </Card>

      {/* 詳細資料 */}
      <Modal title="商品詳細資料" open={!!viewing} onCancel={() => setViewingId(null)} width={820} destroyOnHidden
        footer={viewing && (
          <Space>
            {viewing.code && <Button icon={<SearchOutlined />} loading={autoFilling === viewing.id} onClick={async () => {
              message.info(await fillOne(viewing) ? '已更新書籍資料' : '查不到這個條碼的資料')
            }}>重新查書</Button>}
            <Button icon={<EditOutlined />} onClick={() => { openEdit(viewing); setViewingId(null) }}>編輯</Button>
            <Button type="primary" onClick={() => setViewingId(null)}>關閉</Button>
          </Space>
        )}>
        {viewing && <ProductDetail p={viewing} loading={autoFilling === viewing.id} narrow={isNarrow} />}
      </Modal>

      {/* 編輯 */}
      <Modal title={editing ? `編輯：${editing.sku}` : ''} open={!!editing} onOk={saveEdit} onCancel={() => setEditing(null)}
        okText="儲存" cancelText="取消" destroyOnHidden>
        <Form form={form} layout="vertical">
          <Form.Item name="name" label="商品名稱" rules={[{ required: true, message: '請輸入商品名稱' }]}><Input /></Form.Item>
          <Space wrap>
            <Form.Item name="midCategory" label="中分類"><Select allowClear style={{ width: 200 }} options={mids.map(s => ({ value: s, label: s }))} /></Form.Item>
            <Form.Item name="subCategory" label="子分類"><Select allowClear style={{ width: 240 }} options={subs.map(s => ({ value: s, label: s }))} /></Form.Item>
          </Space>
          <Space wrap>
            <Form.Item name="salePrice" label="售價（NT$）"><InputNumber min={0} precision={0} style={{ width: 140 }} /></Form.Item>
            <Form.Item name="stock" label="庫存"><InputNumber min={0} precision={0} style={{ width: 120 }} /></Form.Item>
            <Form.Item name="isListed" label="上架" valuePropName="checked"><Switch /></Form.Item>
          </Space>
        </Form>
      </Modal>

      {/* 匯入預覽 */}
      <Modal title="匯入預覽" open={!!preview} onCancel={() => setPreview(null)} width={900} destroyOnHidden
        okText={`確認匯入 ${preview?.filter(r => r.status !== 'error').length ?? 0} 筆`} cancelText="取消" onOk={confirmImport}
        okButtonProps={{ disabled: !preview?.some(r => r.status !== 'error'), loading: importing }}>
        {preview && (
          <>
            <Alert style={{ marginBottom: 12 }} type={preview.some(r => r.status === 'error') ? 'warning' : 'info'} showIcon
              message={`共 ${preview.length} 筆：新增 ${preview.filter(r => r.status === 'new').length}、更新 ${preview.filter(r => r.status === 'update').length}、有問題 ${preview.filter(r => r.status === 'error').length}（有問題的不會匯入）`}
              description="店內碼已經存在的會「更新」，不會重複新增。商品名稱用檔案上的；檔案裡售價、庫存空白的，保留原本的值。" />
            <Table<PreviewRow> rowKey="line" size="small" dataSource={preview} pagination={{ pageSize: 10 }} scroll={{ x: 800 }}
              columns={[
                { title: '列', dataIndex: 'line', width: 50 },
                {
                  title: '狀態', dataIndex: 'status', width: 110,
                  render: (s: PreviewRow['status'], r) => s === 'new' ? <Tag color="blue">新增</Tag> : s === 'update' ? <Tag color="green">更新</Tag> : <Tag color="red">{r.problem}</Tag>,
                },
                { title: '店內碼', dataIndex: 'sku', width: 110 },
                { title: '商品名稱', dataIndex: 'name', ellipsis: true },
                { title: '條碼', dataIndex: 'code', width: 130 },
                { title: '子分類', dataIndex: 'sub', width: 180, ellipsis: true },
                { title: '售價', dataIndex: 'salePrice', width: 70 },
                { title: '庫存', dataIndex: 'stock', width: 60 },
              ]} />
          </>
        )}
      </Modal>
    </Space>
  )
}

/**
 * 商品詳細資料（2026-11-08 業主：參考誠品線上的商品頁）
 *   上：左邊大封面；右邊名稱、作者、出版社、出版日期、定價／售價、庫存
 *   中：內容簡介（先顯示幾行，可展開）、目次
 *   下：商品規格（ISBN、頁數、語言、分類、店內碼…）
 * ⭐ 名稱用業主清單上的商品名稱；書籍資料來自「查書補資料」（誠品優先，再來國圖、Google Books 等）。
 */
function ProductDetail({ p, loading, narrow }: { p: Product; loading: boolean; narrow: boolean }) {
  const info = p.info
  const money = (n: number | null) => (n == null ? null : `NT$${n.toLocaleString()}`)
  const dash = <Typography.Text type="secondary">—</Typography.Text>
  const isBook = !info || info.kind !== 'product'
  return (
    <div>
      {loading && <Alert type="info" showIcon style={{ marginBottom: 12 }} message="正在從誠品線上等資料庫查詢書籍資料…" />}
      {!loading && !info && (
        <Alert type="warning" showIcon style={{ marginBottom: 12 }}
          message={p.code ? '查不到這個條碼的書籍資料，可以按下方「重新查書」再試一次。' : '這個商品沒有條碼，查不到書籍資料。'} />
      )}
      <div style={{ display: 'flex', gap: 24, flexWrap: 'wrap', alignItems: 'flex-start' }}>
        <Cover covers={info?.covers ?? []} width={narrow ? 140 : 180} />
        <div style={{ flex: 1, minWidth: 260 }}>
          <Typography.Title level={4} style={{ marginTop: 0, marginBottom: 4 }}>{p.name}</Typography.Title>
          {info?.subtitle && <Typography.Text type="secondary">{info.subtitle}</Typography.Text>}
          <Descriptions size="small" column={1} style={{ marginTop: 10 }} items={isBook ? [
            { key: 'a', label: '作者', children: info?.authors.join('、') || dash },
            { key: 'p', label: '出版社', children: info?.publisher || dash },
            { key: 'd', label: '出版日期', children: info?.date || dash },
          ] : [
            { key: 'b', label: '品牌', children: info?.brand || dash },
            { key: 'q', label: '規格', children: info?.quantity || dash },
          ]} />
          <div style={{ marginTop: 8, padding: '10px 14px', background: 'var(--c-bg-subtle, #f8fafc)', borderRadius: 8 }}>
            <Space size={24} wrap>
              <span>定價：{info?.price ?? '—'}</span>
              <span>售價：<Typography.Text strong style={{ fontSize: 18, color: '#d97706' }}>{money(p.salePrice) ?? '未設定'}</Typography.Text></span>
              <span>庫存：{p.stock}</span>
              {p.isListed ? <Tag color="green">上架</Tag> : <Tag>下架</Tag>}
            </Space>
          </div>
        </div>
      </div>

      <Typography.Title level={5} style={{ marginTop: 20 }}>內容簡介</Typography.Title>
      {info?.description
        ? <Typography.Paragraph ellipsis={{ rows: 6, expandable: true, symbol: '展開全部' }} style={{ whiteSpace: 'pre-wrap', lineHeight: 1.8 }}>{info.description}</Typography.Paragraph>
        : <Typography.Text type="secondary">（沒有簡介）</Typography.Text>}
      {(info?.toc || info?.ingredients) && (
        <Collapse ghost size="small" items={[
          ...(info?.toc ? [{ key: 'toc', label: '目次', children: <div style={{ whiteSpace: 'pre-wrap' }}>{info.toc.replace(/^目次\s*/, '')}</div> }] : []),
          ...(info?.ingredients ? [{ key: 'ing', label: '成分', children: <div style={{ whiteSpace: 'pre-wrap' }}>{info.ingredients}</div> }] : []),
        ]} />
      )}

      <Typography.Title level={5} style={{ marginTop: 16 }}>商品規格</Typography.Title>
      <Descriptions size="small" bordered column={narrow ? 1 : 2} items={[
        { key: 'isbn', label: isBook ? 'ISBN' : '條碼', children: p.code || dash },
        ...(isBook ? [{ key: 'isbn10', label: 'ISBN-10', children: info?.isbn10 || dash }] : []),
        ...(isBook ? [{ key: 'pages', label: '頁數', children: info?.pages ? `${info.pages} 頁` : dash }] : []),
        ...(isBook ? [{ key: 'lang', label: '語言', children: info?.language || dash }] : []),
        { key: 'cat', label: '書籍分類', children: info?.categories.join('、') || dash },
        { key: 'sku', label: '店內碼', children: p.sku },
        { key: 'mid', label: '中分類', children: p.midCategory || dash },
        { key: 'sub', label: '子分類', children: p.subCategory || dash },
        { key: 'src', label: '資料來源', children: info?.sources.join('、') || dash },
      ]} />
    </div>
  )
}
