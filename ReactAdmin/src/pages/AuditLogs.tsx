import { useCallback, useEffect, useState } from 'react'
import { Card, Table, Space, Select, DatePicker, Button, Tag, Typography, message } from 'antd'
import { ReloadOutlined } from '@ant-design/icons'
import type { Dayjs } from 'dayjs'
import { api, ApiError } from '../api'

interface AuditRow {
  id: number
  action: string
  targetType: string
  targetId: number | null
  userId: number | null
  userName: string | null
  beforeJson: string | null
  afterJson: string | null
  createdAt: string
}

const ACTION_COLOR = (action: string) => {
  if (action.endsWith('.create')) return 'green'
  if (action.endsWith('.update') || action.endsWith('.reorder')) return 'blue'
  if (action.endsWith('.delete')) return 'red'
  if (action.includes('password')) return 'orange'
  return 'default'
}

/** 改前/改後 JSON：預設收合三行，可展開看全文 */
function JsonCell({ value }: { value: string | null }) {
  if (!value) return <span style={{ color: 'var(--c-border-strong)' }}>—</span>
  let pretty = value
  try { pretty = JSON.stringify(JSON.parse(value), null, 1) } catch { /* 原樣顯示 */ }
  return (
    <Typography.Paragraph
      style={{ fontSize: 'var(--fs-xs)', marginBottom: 0, whiteSpace: 'pre-wrap', wordBreak: 'break-all' }}
      ellipsis={{ rows: 3, expandable: true, symbol: '展開' }}
    >
      {pretty}
    </Typography.Paragraph>
  )
}

export default function AuditLogs() {
  const [rows, setRows] = useState<AuditRow[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [loading, setLoading] = useState(false)
  const [actionFilter, setActionFilter] = useState<string | undefined>()
  const [targetFilter, setTargetFilter] = useState<string | undefined>()
  const [range, setRange] = useState<[Dayjs | null, Dayjs | null] | null>(null)
  const [options, setOptions] = useState<{ actions: string[]; targetTypes: string[] }>({ actions: [], targetTypes: [] })

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const qs = new URLSearchParams({ pageNumber: String(page), pageSize: String(pageSize) })
      if (actionFilter) qs.set('action', actionFilter)
      if (targetFilter) qs.set('targetType', targetFilter)
      if (range?.[0]) qs.set('from', range[0].format('YYYY-MM-DD'))
      if (range?.[1]) qs.set('to', range[1].format('YYYY-MM-DD'))
      const data = await api.get<{ items: AuditRow[]; total: number }>(`/api/admin/audit-logs?${qs}`)
      setRows(data.items); setTotal(data.total)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '載入失敗')
    } finally { setLoading(false) }
  }, [page, pageSize, actionFilter, targetFilter, range])

  useEffect(() => { load() }, [load])
  useEffect(() => {
    api.get<{ actions: string[]; targetTypes: string[] }>('/api/admin/audit-logs/filter-options')
      .then(setOptions).catch(() => {})
  }, [])

  return (
    <Card
      title="異動紀錄"
      extra={
        <Space wrap>
          <Select allowClear placeholder="動作" style={{ width: 170 }}
            options={options.actions.map(a => ({ value: a, label: a }))}
            onChange={v => { setActionFilter(v); setPage(1) }} />
          <Select allowClear placeholder="對象類型" style={{ width: 130 }}
            options={options.targetTypes.map(t => ({ value: t, label: t }))}
            onChange={v => { setTargetFilter(v); setPage(1) }} />
          <DatePicker.RangePicker onChange={v => { setRange(v as [Dayjs | null, Dayjs | null] | null); setPage(1) }} />
          <Button icon={<ReloadOutlined />} onClick={load} />
        </Space>
      }
    >
      <Typography.Paragraph type="secondary" style={{ marginTop: -8, fontSize: 'var(--fs-sm)' }}>
        系統保留所有敏感操作的軌跡（誰、何時、改了什麼、改前改後），供爭議追溯與評鑑佐證。
      </Typography.Paragraph>

      <Table<AuditRow>
        rowKey="id" loading={loading} dataSource={rows} size="small" scroll={{ x: 1000 }}
        pagination={{
          current: page, pageSize, total, showSizeChanger: true,
          showTotal: t => `共 ${t} 筆`,
          onChange: (p, ps) => { setPage(p); setPageSize(ps) },
        }}
        columns={[
          {
            title: '時間', dataIndex: 'createdAt', width: 150,
            render: v => new Date(v + 'Z').toLocaleString('zh-TW'),
          },
          { title: '操作者', dataIndex: 'userName', width: 100, render: v => v ?? '系統' },
          {
            title: '動作', dataIndex: 'action', width: 150,
            render: v => <Tag color={ACTION_COLOR(v)}>{v}</Tag>,
          },
          {
            title: '對象', key: 'target', width: 120,
            render: (_, row) => `${row.targetType}${row.targetId ? ` #${row.targetId}` : ''}`,
          },
          { title: '變更前', dataIndex: 'beforeJson', render: v => <JsonCell value={v} /> },
          { title: '變更後', dataIndex: 'afterJson', render: v => <JsonCell value={v} /> },
        ]}
      />
    </Card>
  )
}
