/*
 * 採購審核（採購候選的第二階段，2026-11-08）—— 放在「採購候選」頁的第二個分頁。
 *
 * 流程：採購清單勾選送審（Books.tsx）→ 這裡看到採購單 → 有 purchase.approve 的人逐本核准／退回
 *       → 核准的按「上架」填店內碼、分類、售價、庫存 → 變成商品（products）
 * ⭐ 退回的書，後端會放回採購清單（備註註明哪張單退回、原因）。
 * ⭐ 採購單是紀錄：送出後明細不變，只變狀態（業主要「有紀錄每次的採購清單內容」）。
 */
import { useCallback, useEffect, useState } from 'react'
import {
  Alert, Button, Form, Input, InputNumber, Modal, Select, Space, Switch, Table, Tag, Typography, message, Grid,
} from 'antd'
import { CheckOutlined, CloseOutlined, ReloadOutlined, ShopOutlined } from '@ant-design/icons'
import { api, ApiError, auth } from '../api'
import { BookDetail, Cover, type BookInfo } from './Books'

interface OrderRow {
  id: number; orderNo: string; submittedAt: string; submittedBy: string; note: string | null
  count: number; quantity: number; pending: number; approved: number; rejected: number; listed: number
}
type ItemStatus = 'pending' | 'approved' | 'rejected' | 'listed'
interface OrderItem {
  id: number; code: string; kind: string; title: string; authors: string | null; publisher: string | null
  pubDate: string | null; note: string | null; quantity: number; status: ItemStatus; rejectReason: string | null
  decidedBy: string | null; decidedAt: string | null; productId: number | null; productDeleted: boolean; info: BookInfo | null
}
interface OrderDetail { id: number; orderNo: string; submittedAt: string; submittedBy: string; note: string | null; items: OrderItem[] }

const STATUS: Record<ItemStatus, { label: string; color: string }> = {
  pending: { label: '待審', color: 'processing' },
  approved: { label: '核准・待上架', color: 'gold' },
  rejected: { label: '退回', color: 'red' },
  listed: { label: '已上架', color: 'green' },
}

/** 伺服器回的時間是 UTC 但不帶 Z（後台各頁的慣例），補上再轉本地時間 */
const localTime = (s: string | null) =>
  s ? new Date(s + (/[zZ]|[+-]\d\d:?\d\d$/.test(s) ? '' : 'Z')).toLocaleString('zh-TW', { hour12: false }) : ''

export default function PurchaseOrders({ refreshKey }: { refreshKey: number }) {
  const canApprove = auth.hasPerm('purchase.approve')
  const isNarrow = !Grid.useBreakpoint().md
  const [orders, setOrders] = useState<OrderRow[]>([])
  const [loading, setLoading] = useState(false)
  const [detail, setDetail] = useState<OrderDetail | null>(null)
  const [selected, setSelected] = useState<number[]>([])
  const [busy, setBusy] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try { setOrders(await api.get<OrderRow[]>('/api/admin/purchase-orders')) }
    catch (e) { message.error(e instanceof ApiError ? e.message : '載入採購單失敗') }
    finally { setLoading(false) }
  }, [])
  useEffect(() => { load() }, [load, refreshKey])

  const openDetail = async (id: number) => {
    try {
      setDetail(await api.get<OrderDetail>(`/api/admin/purchase-orders/${id}`))
      setSelected([])
    } catch (e) { message.error(e instanceof ApiError ? e.message : '載入採購單失敗') }
  }
  const reloadDetail = async () => { if (detail) await openDetail(detail.id); load() }

  // ---------- 核准／退回 ----------
  const [rejectOpen, setRejectOpen] = useState(false)
  const [reason, setReason] = useState('')
  const decide = async (approve: boolean, ids: number[], why?: string) => {
    if (!detail || !ids.length) return
    setBusy(true)
    try {
      const r = await api.post<{ count: number }>(`/api/admin/purchase-orders/${detail.id}/decide`, { itemIds: ids, approve, reason: why ?? null })
      message.success(approve ? `已核准 ${r.count} 本` : `已退回 ${r.count} 本（已放回採購清單）`)
      setRejectOpen(false); setReason('')
      await reloadDetail()
    } catch (e) { message.error(e instanceof ApiError ? e.message : '處理失敗') }
    finally { setBusy(false) }
  }

  // ---------- 上架（核准的 → 商品）----------
  const [listing, setListing] = useState<OrderItem | null>(null)
  const [cats, setCats] = useState<{ mids: string[]; subs: string[] }>({ mids: [], subs: [] })
  const [form] = Form.useForm()
  const openListing = async (it: OrderItem) => {
    setListing(it)
    form.setFieldsValue({ sku: '', name: it.title, midCategory: undefined, subCategory: undefined, salePrice: null, stock: it.quantity, isListed: true })
    try {
      const r = await api.get<{ mids: string[]; subs: string[] }>('/api/admin/products?pageSize=1')
      setCats({ mids: r.mids, subs: r.subs })
    } catch { /* 分類清單拿不到就讓使用者自己打 */ }
  }
  const saveListing = async () => {
    if (!listing) return
    const v = await form.validateFields()
    setBusy(true)
    try {
      await api.post(`/api/admin/purchase-orders/items/${listing.id}/list`, {
        sku: v.sku, name: v.name, midCategory: v.midCategory ?? null, subCategory: v.subCategory ?? null,
        salePrice: v.salePrice ?? null, stock: v.stock ?? 0, isListed: !!v.isListed,
      })
      message.success(`已上架成商品（店內碼 ${v.sku}）`)
      setListing(null)
      await reloadDetail()
    } catch (e) { message.error(e instanceof ApiError ? e.message : '上架失敗') }
    finally { setBusy(false) }
  }

  const statusTags = (o: OrderRow) => (
    <Space size={[4, 4]} wrap>
      {o.pending > 0 && <Tag color="processing">待審 {o.pending}</Tag>}
      {o.approved > 0 && <Tag color="gold">待上架 {o.approved}</Tag>}
      {o.listed > 0 && <Tag color="green">已上架 {o.listed}</Tag>}
      {o.rejected > 0 && <Tag color="red">退回 {o.rejected}</Tag>}
    </Space>
  )
  const pendingIds = detail?.items.filter(i => i.status === 'pending').map(i => i.id) ?? []

  return (
    <>
      {!canApprove && (
        <Alert type="info" showIcon style={{ marginBottom: 12 }}
          message="你可以看採購單和把核准的書上架；核准／退回要有「採購審核」權限（目前只有系統管理員）。" />
      )}
      <Space style={{ marginBottom: 12 }}>
        <Button icon={<ReloadOutlined />} onClick={load}>重新整理</Button>
        <Typography.Text type="secondary">共 {orders.length} 張採購單</Typography.Text>
      </Space>
      <Table<OrderRow>
        rowKey="id" size="small" loading={loading} dataSource={orders} pagination={{ pageSize: 20, showSizeChanger: false }}
        locale={{ emptyText: '還沒有採購單。到「採購清單」分頁勾選要買的書，按「送審」。' }}
        columns={isNarrow ? [
          {
            title: '採購單', key: 'main', render: (_, o) => (
              <div style={{ lineHeight: 1.6 }}>
                <a onClick={() => openDetail(o.id)} style={{ fontWeight: 600 }}>{o.orderNo}</a>
                <div style={{ fontSize: 12 }}><Typography.Text type="secondary">{localTime(o.submittedAt)}・{o.submittedBy}・{o.count} 本／{o.quantity} 冊</Typography.Text></div>
                {statusTags(o)}
              </div>
            ),
          },
        ] : [
          { title: '採購單號', dataIndex: 'orderNo', width: 160, render: (t: string, o) => <a onClick={() => openDetail(o.id)}>{t}</a> },
          { title: '送審時間', dataIndex: 'submittedAt', width: 170, render: (s: string) => localTime(s) },
          { title: '送審人', dataIndex: 'submittedBy', width: 110 },
          { title: '本數', dataIndex: 'count', width: 70 },
          { title: '總冊數', dataIndex: 'quantity', width: 80 },
          { title: '狀態', key: 'status', render: (_, o) => statusTags(o) },
          { title: '說明', dataIndex: 'note', ellipsis: true },
        ]}
      />

      {/* 採購單明細 */}
      <Modal title={detail ? `採購單 ${detail.orderNo}` : ''} open={!!detail} onCancel={() => setDetail(null)} width={980} destroyOnHidden
        footer={<Button type="primary" onClick={() => setDetail(null)}>關閉</Button>}>
        {detail && (
          <>
            <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
              送審：{detail.submittedBy}・{localTime(detail.submittedAt)}{detail.note ? `・說明：${detail.note}` : ''}
            </Typography.Paragraph>
            {canApprove && pendingIds.length > 0 && (
              <Space wrap style={{ marginBottom: 12 }}>
                <Button type="primary" icon={<CheckOutlined />} disabled={!selected.length} loading={busy} onClick={() => decide(true, selected)}>
                  核准所選（{selected.length}）
                </Button>
                <Button danger icon={<CloseOutlined />} disabled={!selected.length} onClick={() => setRejectOpen(true)}>退回所選（{selected.length}）</Button>
                <Button onClick={() => setSelected(pendingIds)}>全選待審（{pendingIds.length}）</Button>
              </Space>
            )}
            <Table<OrderItem>
              rowKey="id" size="small" dataSource={detail.items} pagination={false} scroll={isNarrow ? undefined : { x: 900 }}
              rowSelection={canApprove && pendingIds.length ? {
                selectedRowKeys: selected, onChange: keys => setSelected(keys as number[]),
                getCheckboxProps: r => ({ disabled: r.status !== 'pending' }),
              } : undefined}
              expandable={{
                expandedRowRender: r => r.info ? <BookDetail info={r.info} /> : <Typography.Text type="secondary">沒有查書資料</Typography.Text>,
              }}
              columns={[
                { title: '', key: 'cover', width: 52, render: (_, r) => <Cover covers={r.info?.covers ?? []} width={36} fixed /> },
                {
                  title: '書名', dataIndex: 'title', render: (t: string, r) => (
                    <div style={{ lineHeight: 1.5 }}>
                      <div style={{ fontWeight: 600 }}>{t}</div>
                      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                        {[r.authors, r.publisher, r.code].filter(Boolean).join('・')}
                      </Typography.Text>
                      {r.note && <div style={{ fontSize: 12 }}><Typography.Text type="secondary">備註：{r.note}</Typography.Text></div>}
                    </div>
                  ),
                },
                { title: '定價', key: 'price', width: 100, render: (_, r) => r.info?.price ?? '' },
                { title: '數量', dataIndex: 'quantity', width: 60 },
                {
                  title: '狀態', dataIndex: 'status', width: 200, render: (s: ItemStatus, r) => (
                    <div>
                      <Tag color={STATUS[s].color}>{STATUS[s].label}</Tag>
                      {r.productDeleted && <Tag>商品已刪除</Tag>}
                      {r.decidedBy && <div style={{ fontSize: 12 }}><Typography.Text type="secondary">{r.decidedBy}・{localTime(r.decidedAt)}</Typography.Text></div>}
                      {r.rejectReason && <div style={{ fontSize: 12 }}><Typography.Text type="danger">原因：{r.rejectReason}</Typography.Text></div>}
                    </div>
                  ),
                },
                {
                  title: '操作', key: 'op', width: 90, render: (_, r) => r.status === 'approved'
                    ? <Button size="small" type="primary" icon={<ShopOutlined />} onClick={() => openListing(r)}>上架</Button>
                    : null,
                },
              ]}
            />
          </>
        )}
      </Modal>

      {/* 退回原因 */}
      <Modal title={`退回 ${selected.length} 本`} open={rejectOpen} onCancel={() => setRejectOpen(false)} okText="退回" okButtonProps={{ danger: true, loading: busy, disabled: !reason.trim() }}
        cancelText="取消" onOk={() => decide(false, selected, reason)} destroyOnHidden>
        <Typography.Paragraph type="secondary">退回的書會放回「採購清單」，備註會註明哪張採購單退回和原因。</Typography.Paragraph>
        <Input.TextArea value={reason} onChange={e => setReason(e.target.value)} rows={3} maxLength={500} placeholder="退回原因（必填）" autoFocus />
      </Modal>

      {/* 上架成商品 */}
      <Modal title={listing ? `上架成商品：${listing.title}` : ''} open={!!listing} onCancel={() => setListing(null)} onOk={saveListing}
        okText="上架" cancelText="取消" okButtonProps={{ loading: busy }} destroyOnHidden>
        <Form form={form} layout="vertical">
          <Form.Item name="sku" label="店內碼" rules={[{ required: true, message: '請輸入店內碼' }]}><Input placeholder="例：1102260034" /></Form.Item>
          <Form.Item name="name" label="商品名稱" rules={[{ required: true, message: '請輸入商品名稱' }]}><Input /></Form.Item>
          <Space wrap>
            <Form.Item name="midCategory" label="中分類">
              <Select allowClear showSearch style={{ width: 200 }} options={cats.mids.map(s => ({ value: s, label: s }))} />
            </Form.Item>
            <Form.Item name="subCategory" label="子分類">
              <Select allowClear showSearch style={{ width: 240 }} options={cats.subs.map(s => ({ value: s, label: s }))} />
            </Form.Item>
          </Space>
          <Space wrap>
            <Form.Item name="salePrice" label="售價（NT$）"><InputNumber min={0} precision={0} style={{ width: 140 }} /></Form.Item>
            <Form.Item name="stock" label="庫存（預設＝採購數量）"><InputNumber min={0} precision={0} style={{ width: 160 }} /></Form.Item>
            <Form.Item name="isListed" label="上架（前台查得到）" valuePropName="checked"><Switch /></Form.Item>
          </Space>
        </Form>
      </Modal>
    </>
  )
}
