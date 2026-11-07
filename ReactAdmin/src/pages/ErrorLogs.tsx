import { useCallback, useEffect, useState } from 'react'
import {
  Card, Table, Space, Select, DatePicker, Input, Button, Tag,
  Typography, message, Statistic, Row, Col,
} from 'antd'
import { ReloadOutlined } from '@ant-design/icons'
import type { Dayjs } from 'dayjs'
import { api, ApiError } from '../api'
import GuideAlert from '../components/GuideAlert'

interface ErrorRow {
  id: number
  createdAt: string
  userName: string | null
  method: string
  path: string
  statusCode: number
  errorCode: string | null
  message: string | null
  exceptionType: string | null
  stackTrace: string | null
  traceId: string | null
}

interface Summary {
  total: number
  serverErrors: number
  topPaths: { path: string; count: number }[]
  retentionDays: number
  oldestAt: string | null
}

const statusColor = (code: number) => {
  if (code >= 500) return 'error'
  if (code === 403) return 'purple'
  if (code === 404) return 'default'
  return 'warning'
}

export default function ErrorLogs() {
  const [rows, setRows] = useState<ErrorRow[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [statusFilter, setStatusFilter] = useState<number | undefined>()
  const [serverOnly, setServerOnly] = useState(false)
  const [range, setRange] = useState<[Dayjs | null, Dayjs | null] | null>(null)
  const [summary, setSummary] = useState<Summary | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const qs = new URLSearchParams({ pageNumber: String(page), pageSize: String(pageSize) })
      if (keyword.trim()) qs.set('keyword', keyword.trim())
      if (statusFilter) qs.set('statusCode', String(statusFilter))
      if (serverOnly) qs.set('serverErrorOnly', 'true')
      if (range?.[0]) qs.set('from', range[0].format('YYYY-MM-DD'))
      if (range?.[1]) qs.set('to', range[1].format('YYYY-MM-DD'))
      const data = await api.get<{ items: ErrorRow[]; total: number }>(`/api/admin/error-logs?${qs}`)
      setRows(data.items); setTotal(data.total)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '載入失敗')
    } finally { setLoading(false) }
  }, [page, pageSize, keyword, statusFilter, serverOnly, range])

  useEffect(() => { load() }, [load])
  useEffect(() => { api.get<Summary>('/api/admin/error-logs/summary').then(setSummary).catch(() => {}) }, [])

  return (
    <Card
      title="錯誤日誌"
      extra={
        <Space wrap>
          <Input.Search allowClear placeholder="路徑 / 訊息 / 錯誤代碼" style={{ width: 220 }}
            onSearch={v => { setKeyword(v); setPage(1) }} />
          <Select allowClear placeholder="狀態碼" style={{ width: 120 }}
            options={[400, 403, 404, 500].map(c => ({ value: c, label: String(c) }))}
            onChange={v => { setStatusFilter(v); setPage(1) }} />
          <Select value={serverOnly} style={{ width: 130 }}
            options={[
              { value: false, label: '全部錯誤' },
              { value: true, label: '僅系統錯誤' },
            ]}
            onChange={v => { setServerOnly(v); setPage(1) }} />
          <DatePicker.RangePicker onChange={v => { setRange(v as [Dayjs | null, Dayjs | null] | null); setPage(1) }} />
          <Button icon={<ReloadOutlined />} onClick={load} />
        </Space>
      }
    >
      {/* 保留天數與最早一筆會隨資料變，且直接決定「查不查得到」，所以放 live 不收 */}
      <GuideAlert id="errorlogs-intro" style={{ marginBottom: 14 }}
        title="這裡記錄「失敗的請求」；成功的業務異動請看「異動紀錄」"
        live={summary && (
          <div>
            本日誌僅保留 <b>{summary.retentionDays}</b> 天，逾期自動清除
            {summary.oldestAt && `（目前最早一筆：${new Date(summary.oldestAt + 'Z').toLocaleDateString('zh-TW')}）`}
            ；異動紀錄則永久保存，不受此清理影響。
          </div>
        )}>
        錯誤日誌以獨立交易寫入，即使操作因錯誤而回滾，紀錄仍會保留。系統錯誤（500）另附例外類型與堆疊供診斷。
      </GuideAlert>

      {summary && (
        <Row gutter={16} style={{ marginBottom: 16 }}>
          <Col xs={12} sm={6}>
            <Statistic title="近 7 日錯誤" value={summary.total} suffix="筆" />
          </Col>
          <Col xs={12} sm={6}>
            <Statistic title="其中系統錯誤" value={summary.serverErrors} suffix="筆"
              valueStyle={{ color: summary.serverErrors > 0 ? 'var(--c-danger)' : undefined }} />
          </Col>
          <Col xs={24} sm={12}>
            <div style={{ fontSize: 'var(--fs-sm)', color: 'var(--c-text-secondary)', marginBottom: 4 }}>錯誤最多的路徑</div>
            <Space direction="vertical" size={2}>
              {summary.topPaths.slice(0, 3).map(p => (
                <Typography.Text key={p.path} style={{ fontSize: 'var(--fs-sm)' }}>
                  <Tag>{p.count}</Tag><code>{p.path}</code>
                </Typography.Text>
              ))}
              {summary.topPaths.length === 0 && <Typography.Text type="secondary" style={{ fontSize: 'var(--fs-sm)' }}>近 7 日無錯誤</Typography.Text>}
            </Space>
          </Col>
        </Row>
      )}

      <Table<ErrorRow>
        rowKey="id" loading={loading} dataSource={rows} size="small" scroll={{ x: 900 }}
        pagination={{
          current: page, pageSize, total, showSizeChanger: true,
          showTotal: t => `共 ${t} 筆`,
          onChange: (p, ps) => { setPage(p); setPageSize(ps) },
        }}
        expandable={{
          rowExpandable: r => Boolean(r.stackTrace || r.traceId),
          expandedRowRender: r => (
            <div style={{ fontSize: 'var(--fs-xs)' }}>
              {r.traceId && <div style={{ marginBottom: 6 }}>追蹤代碼：<code>{r.traceId}</code></div>}
              {r.exceptionType && <div style={{ marginBottom: 6 }}>例外類型：<code>{r.exceptionType}</code></div>}
              {r.stackTrace && (
                <Typography.Paragraph
                  style={{ fontSize: 'var(--fs-xs)', whiteSpace: 'pre-wrap', wordBreak: 'break-all', marginBottom: 0, background: 'var(--c-bg-subtle)', padding: 10, borderRadius: 6 }}
                  ellipsis={{ rows: 8, expandable: true, symbol: '展開完整堆疊' }}
                >
                  {r.stackTrace}
                </Typography.Paragraph>
              )}
            </div>
          ),
        }}
        columns={[
          {
            title: '時間', dataIndex: 'createdAt', width: 150,
            render: v => new Date(v + 'Z').toLocaleString('zh-TW'),
          },
          {
            title: '狀態', dataIndex: 'statusCode', width: 80,
            render: v => <Tag color={statusColor(v)}>{v}</Tag>,
          },
          { title: '使用者', dataIndex: 'userName', width: 100, render: v => v ?? '未登入' },
          {
            title: '請求', key: 'req', width: 260,
            render: (_, r) => (
              <span style={{ fontSize: 'var(--fs-xs)' }}>
                <Tag style={{ margin: 0, marginRight: 4 }}>{r.method}</Tag>
                <code>{r.path}</code>
              </span>
            ),
          },
          {
            title: '錯誤代碼', dataIndex: 'errorCode', width: 130,
            render: v => (v ? <Tag color="orange">{v}</Tag> : '—'),
          },
          { title: '訊息', dataIndex: 'message', render: v => v ?? '—' },
        ]}
      />
    </Card>
  )
}
