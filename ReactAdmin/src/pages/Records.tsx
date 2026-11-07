import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  Card, Table, Button, Select, Space, DatePicker, Drawer, Input, InputNumber,
  message, Tag, Popconfirm, Typography, Checkbox, Spin, Empty, Tabs, Badge,
  Timeline, Modal, Alert,
} from 'antd'
import {
  PlusOutlined, ReloadOutlined, EditOutlined, EyeOutlined, DeleteOutlined,
  SendOutlined, CheckOutlined, CloseOutlined, ClockCircleOutlined,
  RollbackOutlined, StopOutlined,
} from '@ant-design/icons'
import dayjs, { type Dayjs } from 'dayjs'
import { api, ApiError, auth } from '../api'
import FileAttachments from '../components/FileAttachments'
import { useTabParam } from '../useTabParam'
import {
  type FieldDef, type SectionDef, type FormSchema, type Content, type RowData,
  visibleBy, missingRequired, applyCompute, deriveContent,
} from '../formSchema'

// ===== 型別 =====

interface FormDef { id: number; code: string; name: string; category: string | null; frequency: string }

type RecordStatus = 'Draft' | 'InReview' | 'Submitted' | 'Cancelled'

interface RecordRow {
  id: number
  formDefinitionId: number
  formName: string
  recordDate: string
  status: RecordStatus
  currentStepName: string | null
  wasRejected: boolean
  createdBy: string | null
  isOwner: boolean
  updatedAt: string
  submittedAt: string | null
}

interface ApprovalStep {
  round: number
  stepOrder: number
  name: string
  isNamed: boolean
  status: 'Pending' | 'Approved' | 'Rejected'
  actedByName: string | null
  onBehalfOfName: string | null
  actedAt: string | null
  comment: string | null
}

interface FullRecord {
  id: number
  formDefinitionId: number
  formName: string
  recordDate: string
  createdAt?: string
  status: RecordStatus
  contentJson: string
  tags: string[]
  currentStep: number | null
  canApprove: boolean
  approveOnBehalfOfName: string | null
  canWithdraw: boolean
  isOwner: boolean
  wasRejected: boolean
  cancelReason: string | null
  approvalSteps: ApprovalStep[]
}

// ===== 欄位渲染 =====

function FieldControl({ field, value, onChange, disabled }: {
  field: FieldDef; value: unknown; onChange: (v: unknown) => void; disabled: boolean
}) {
  const opts = (field.options ?? []).map(o => ({ value: o, label: o }))
  switch (field.type) {
    case 'textarea':
      return <Input.TextArea rows={2} value={(value as string) ?? ''} disabled={disabled} placeholder={field.placeholder}
        onChange={e => onChange(e.target.value)} />
    case 'number':
      return <InputNumber style={{ width: '100%' }} value={(value as number) ?? null} disabled={disabled}
        onChange={v => onChange(v)} />
    case 'date':
      return <DatePicker style={{ width: '100%' }} value={value ? dayjs(value as string) : null} disabled={disabled}
        onChange={(d: Dayjs | null) => onChange(d ? d.format('YYYY-MM-DD') : null)} />
    case 'select':
      return <Select style={{ width: '100%' }} allowClear value={(value as string) ?? undefined} disabled={disabled}
        options={opts} onChange={v => onChange(v ?? null)} />
    case 'checklist':
      return <Checkbox.Group value={(value as string[]) ?? []} disabled={disabled}
        options={opts}
        onChange={v => onChange(v)} />
    default:
      return <Input value={(value as string) ?? ''} disabled={disabled} placeholder={field.placeholder}
        onChange={e => onChange(e.target.value)} />
  }
}

/**
 * 多列表格（section.type = 'rows'）：明細、追蹤事項這種「一張表好幾列」的內容。
 * 內容存成 content[key] 的陣列。
 */
function RowsSection({ section, rows, onChange, disabled }: {
  section: SectionDef; rows: RowData[]; onChange: (rows: RowData[]) => void; disabled: boolean
}) {
  const cols = section.columns ?? []
  return (
    <div>
      {rows.length === 0 && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="還沒有資料" />}
      {rows.map((row, ri) => (
        <Card key={ri} size="small" type="inner" style={{ marginBottom: 10 }}
          title={<Typography.Text type="secondary">第 {ri + 1} 列</Typography.Text>}
          extra={!disabled && (
            <Popconfirm title="刪除這一列？" onConfirm={() => onChange(rows.filter((_, i) => i !== ri))}>
              <Button size="small" danger type="text" icon={<DeleteOutlined />} />
            </Popconfirm>
          )}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 10 }}>
            {cols.map(col => (
              <div key={col.key} style={col.type === 'textarea' || col.type === 'checklist' ? { gridColumn: '1 / -1' } : undefined}>
                <Typography.Text type="secondary" style={{ fontSize: 'var(--fs-sm)' }}>{col.label}</Typography.Text>
                <FieldControl field={col} value={row[col.key]} disabled={disabled || !!col.compute}
                  onChange={v => onChange(rows.map((r, i) => i === ri ? applyCompute(cols, { ...r, [col.key]: v }) : r))} />
              </div>
            ))}
          </div>
        </Card>
      ))}
      {!disabled && (
        <Button icon={<PlusOutlined />} onClick={() => onChange([...rows, {}])}>
          {section.addLabel ?? '新增一列'}
        </Button>
      )}
    </div>
  )
}

function statusTag(row: { status: string; wasRejected: boolean; currentStepName?: string | null }) {
  if (row.status === 'Submitted') return <Tag color="success">已完成</Tag>
  if (row.status === 'Cancelled') return <Tag color="default">已撤銷</Tag>
  if (row.status === 'InReview')
    return <Tag color="processing">簽核中{row.currentStepName ? ` · ${row.currentStepName}` : ''}</Tag>
  return (
    <Space size={4}>
      <Tag color="warning">草稿</Tag>
      {row.wasRejected && <Tag color="error">被退件</Tag>}
    </Space>
  )
}

// ===== 主頁 =====

export default function Records() {
  const canWrite = auth.hasPerm('record.write')
  const [tab, setTab] = useTabParam('all', ['all', 'pending']) as [
    'all' | 'pending', (v: 'all' | 'pending') => void
  ]
  const [defs, setDefs] = useState<FormDef[]>([])
  const [rows, setRows] = useState<RecordRow[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [loading, setLoading] = useState(false)
  const [pendingCount, setPendingCount] = useState(0)
  const [defFilter, setDefFilter] = useState<number | undefined>()
  const [statusFilter, setStatusFilter] = useState<string | undefined>()
  const [range, setRange] = useState<[Dayjs | null, Dayjs | null] | null>(null)
  const [selectedIds, setSelectedIds] = useState<number[]>([])

  // 編輯器狀態
  const [editorOpen, setEditorOpen] = useState(false)
  const [editorDef, setEditorDef] = useState<FormDef | null>(null)
  const [schema, setSchema] = useState<FormSchema | null>(null)
  const [record, setRecord] = useState<FullRecord | null>(null)
  const [recordDate, setRecordDate] = useState<Dayjs>(dayjs())
  const [content, setContent] = useState<Content>({})
  const [tags, setTags] = useState<string[]>([])
  const [readOnly, setReadOnly] = useState(false)

  // 自動計算 —— 內容一變就重算；算完沒變就不動，不會一直重繪
  useEffect(() => {
    if (!schema) return
    const d = deriveContent(schema, content)
    if (d !== content) setContent(d)
  }, [schema, content])

  const [saving, setSaving] = useState(false)

  const refreshPendingCount = useCallback(() => {
    api.get<{ total: number }>('/api/admin/work-records/pending-approvals?pageNumber=1&pageSize=1')
      .then(d => setPendingCount(d.total)).catch(() => {})
  }, [])

  const load = useCallback(async (p = page, ps = pageSize) => {
    setLoading(true)
    try {
      const qs = new URLSearchParams({ pageNumber: String(p), pageSize: String(ps) })
      let url = '/api/admin/work-records'
      if (tab === 'pending') {
        url = '/api/admin/work-records/pending-approvals'
      } else {
        if (defFilter) qs.set('formDefinitionId', String(defFilter))
        if (statusFilter) qs.set('status', statusFilter)
        if (range?.[0]) qs.set('from', range[0].format('YYYY-MM-DD'))
        if (range?.[1]) qs.set('to', range[1].format('YYYY-MM-DD'))
      }
      const data = await api.get<{ items: RecordRow[]; total: number }>(`${url}?${qs}`)
      setRows(data.items); setTotal(data.total)
      setSelectedIds([])
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '載入失敗')
    } finally { setLoading(false) }
  }, [page, pageSize, tab, defFilter, statusFilter, range])

  useEffect(() => { load() }, [load])
  // 從別頁連過來直接打開某一筆（例：首頁待辦的連結 /records?open=123）
  useEffect(() => {
    const id = Number(new URLSearchParams(window.location.search).get('open'))
    if (!id) return
    api.get<FullRecord>(`/api/admin/work-records/${id}`)
      .then(r => openExisting({ id, formDefinitionId: r.formDefinitionId } as RecordRow))
      .catch(() => message.error('找不到這筆記錄，或你沒有權限看'))
  }, []) // eslint-disable-line react-hooks/exhaustive-deps
  // 從別頁直接開某張表單的新增畫面（例：/records?form=表單代碼）
  useEffect(() => {
    const code = new URLSearchParams(window.location.search).get('form')
    if (!code || !canWrite || defs.length === 0) return
    const def = defs.find(d => d.code === code)
    if (def) openNew(def)
    else message.warning('找不到這張表單')
  }, [defs]) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    api.get<FormDef[]>('/api/admin/form-definitions').then(setDefs).catch(() => {})
    refreshPendingCount()
  }, [refreshPendingCount])

  // 表單選單依分類分組（表單一多，平鋪的清單會找不到）
  const groupedDefs = useMemo(() => {
    const groups = new Map<string, { value: number; label: string }[]>()
    for (const d of defs) {
      const k = d.category || '其他'
      if (!groups.has(k)) groups.set(k, [])
      groups.get(k)!.push({ value: d.id, label: d.name })
    }
    return [...groups].map(([label, options]) => ({ label, title: label, options }))
  }, [defs])

  // ===== 開啟編輯器 =====

  const openNew = async (def: FormDef) => {
    try {
      const full = await api.get<{ schemaJson: string }>(`/api/admin/form-definitions/${def.id}`)
      setEditorDef(def)
      setSchema(JSON.parse(full.schemaJson) as FormSchema)
      setRecord(null)
      setRecordDate(dayjs())
      setContent({})
      setTags([])
      setReadOnly(false)
      setEditorOpen(true)
    } catch {
      message.error('無法載入表單定義')
    }
  }

  const openExisting = async (row: RecordRow) => {
    try {
      const [full, def] = await Promise.all([
        api.get<FullRecord>(`/api/admin/work-records/${row.id}`),
        api.get<{ schemaJson: string } & FormDef>(`/api/admin/form-definitions/${row.formDefinitionId}`),
      ])
      setEditorDef(def)
      setSchema(JSON.parse(def.schemaJson) as FormSchema)
      setRecord(full)
      setRecordDate(dayjs(full.recordDate))
      setContent(JSON.parse(full.contentJson) as Content)
      setTags(full.tags)
      setReadOnly(full.status !== 'Draft' || !canWrite)
      setEditorOpen(true)
    } catch {
      message.error('無法載入記錄')
    }
  }

  // ===== 儲存/送簽 =====

  const save = async (submit: boolean) => {
    if (!editorDef) return
    // 必填只在送出時擋（草稿可以先存一半）
    if (submit && schema) {
      const miss = missingRequired(schema, content)
      if (miss.length) { message.warning(`請先填：${miss.join('、')}`); return }
    }
    setSaving(true)
    try {
      const body = {
        formDefinitionId: editorDef.id,
        recordDate: recordDate.format('YYYY-MM-DD'),
        contentJson: JSON.stringify(content),
        tags,
      }
      let id = record?.id ?? null
      if (id == null) {
        const res = await api.post<{ id: number }>('/api/admin/work-records', body)
        id = res.id
      } else {
        await api.put(`/api/admin/work-records/${id}`, body)
      }
      if (submit) {
        const res = await api.post<{ status: string }>(`/api/admin/work-records/${id}/submit`)
        message.success(res.status === 'InReview' ? '已送簽，等待簽核' : '記錄已完成')
      } else {
        message.success('草稿已儲存')
      }
      setEditorOpen(false)
      load(); refreshPendingCount()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '儲存失敗')
    } finally { setSaving(false) }
  }

  const remove = async (row: RecordRow) => {
    try {
      await api.del(`/api/admin/work-records/${row.id}`)
      message.success('草稿已刪除')
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '刪除失敗')
    }
  }

  // ===== 簽核動作 =====

  const doApprove = (id: number) => {
    let comment = ''
    Modal.confirm({
      title: '同意此記錄？',
      content: <Input.TextArea rows={2} placeholder="意見（選填）" onChange={e => { comment = e.target.value }} />,
      okText: '同意', okType: 'primary',
      onOk: async () => {
        try {
          await api.post(`/api/admin/work-records/${id}/approve`, { comment })
          message.success('已簽核')
          setEditorOpen(false)
          load(); refreshPendingCount()
        } catch (e) {
          message.error(e instanceof ApiError ? e.message : '簽核失敗')
        }
      },
    })
  }

  const doReject = (id: number) => {
    let comment = ''
    Modal.confirm({
      title: '退件此記錄？',
      content: (
        <div>
          <Typography.Paragraph type="secondary" style={{ fontSize: 'var(--fs-sm)' }}>
            記錄將退回填寫者修改後重新送簽；退件意見必填。
          </Typography.Paragraph>
          <Input.TextArea rows={3} placeholder="退件意見（必填）" onChange={e => { comment = e.target.value }} />
        </div>
      ),
      okText: '退件', okType: 'danger',
      onOk: async () => {
        if (!comment.trim()) {
          message.warning('請填寫退件意見')
          return Promise.reject()
        }
        try {
          await api.post(`/api/admin/work-records/${id}/reject`, { comment })
          message.success('已退件')
          setEditorOpen(false)
          load(); refreshPendingCount()
        } catch (e) {
          message.error(e instanceof ApiError ? e.message : '退件失敗')
        }
      },
    })
  }

  const doWithdraw = async (id: number) => {
    try {
      await api.post(`/api/admin/work-records/${id}/withdraw`)
      message.success('已抽回，記錄回到草稿')
      setEditorOpen(false)
      load(); refreshPendingCount()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '抽單失敗')
    }
  }

  const doCancel = (id: number) => {
    let comment = ''
    Modal.confirm({
      title: '撤銷此已完成記錄？',
      content: (
        <div>
          <Typography.Paragraph type="secondary" style={{ fontSize: 'var(--fs-sm)' }}>
            撤銷後記錄將標示為已撤銷並保留於系統中（不會刪除），需填寫原因。
          </Typography.Paragraph>
          <Input.TextArea rows={3} placeholder="撤銷原因（必填）" onChange={e => { comment = e.target.value }} />
        </div>
      ),
      okText: '撤銷', okType: 'danger',
      onOk: async () => {
        if (!comment.trim()) {
          message.warning('請填寫撤銷原因')
          return Promise.reject()
        }
        try {
          await api.post(`/api/admin/work-records/${id}/cancel`, { comment })
          message.success('已撤銷')
          setEditorOpen(false)
          load()
        } catch (e) {
          message.error(e instanceof ApiError ? e.message : '撤銷失敗')
        }
      },
    })
  }

  const batchApprove = async () => {
    try {
      const res = await api.post<{ results: { id: number; ok: boolean }[] }>(
        '/api/admin/work-records/approve-batch', { ids: selectedIds, comment: '' })
      const ok = res.results.filter(r => r.ok).length
      message.success(`已簽核 ${ok} 筆${ok < res.results.length ? `，${res.results.length - ok} 筆失敗` : ''}`)
      load(); refreshPendingCount()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '批次簽核失敗')
    }
  }

  // ===== 簽核時間軸 =====

  const approvalTimeline = (steps: ApprovalStep[]) => {
    const rounds = [...new Set(steps.map(s => s.round))].sort((a, b) => a - b)
    return rounds.map(round => (
      <div key={round} style={{ marginBottom: 8 }}>
        {rounds.length > 1 && (
          <Typography.Text type="secondary" style={{ fontSize: 'var(--fs-sm)' }}>第 {round} 次送簽</Typography.Text>
        )}
        <Timeline
          style={{ marginTop: 8 }}
          items={steps.filter(s => s.round === round).map(s => ({
            color: s.status === 'Approved' ? 'green' : s.status === 'Rejected' ? 'red' : 'gray',
            dot: s.status === 'Pending' ? <ClockCircleOutlined /> : undefined,
            children: (
              <div>
                <Space size={6}>
                  <Typography.Text strong style={{ fontSize: 'var(--fs-md)' }}>{s.name}</Typography.Text>
                  {s.isNamed && <Tag color="blue" style={{ margin: 0, fontSize: 'var(--fs-xs)' }}>指名</Tag>}
                  {s.status === 'Approved' && <Tag color="success" style={{ margin: 0 }}>同意</Tag>}
                  {s.status === 'Rejected' && <Tag color="error" style={{ margin: 0 }}>退件</Tag>}
                  {s.status === 'Pending' && <Tag style={{ margin: 0 }}>待簽</Tag>}
                </Space>
                {s.actedByName && (
                  <div style={{ fontSize: 'var(--fs-sm)', color: 'var(--c-text-secondary)' }}>
                    {s.actedByName}
                    {s.onBehalfOfName && (
                      <Tag color="purple" style={{ margin: '0 0 0 6px', fontSize: 'var(--fs-xs)' }}>
                        代 {s.onBehalfOfName} 簽核
                      </Tag>
                    )}
                    {' · '}{s.actedAt ? new Date(s.actedAt + 'Z').toLocaleString('zh-TW') : ''}
                  </div>
                )}
                {s.comment && <div style={{ fontSize: 'var(--fs-sm)' }}>{s.comment}</div>}
              </div>
            ),
          }))}
        />
      </div>
    ))
  }

  // ===== 渲染 =====

  return (
    <Card
      title="工作記錄"
      extra={
        <Space wrap>
          {tab === 'all' && (
            <>
              <Select allowClear placeholder="表單" style={{ width: 200 }} showSearch optionFilterProp="label"
                options={groupedDefs}
                onChange={v => { setDefFilter(v); setPage(1) }} />
              <Select allowClear placeholder="狀態" style={{ width: 120 }}
                options={[
                  { value: 'Draft', label: '草稿' },
                  { value: 'InReview', label: '簽核中' },
                  { value: 'Submitted', label: '已完成' },
                  { value: 'Cancelled', label: '已撤銷' },
                ]}
                onChange={v => { setStatusFilter(v); setPage(1) }} />
              <DatePicker.RangePicker onChange={v => { setRange(v as [Dayjs | null, Dayjs | null] | null); setPage(1) }} />
            </>
          )}
          <Button icon={<ReloadOutlined />} onClick={() => { load(); refreshPendingCount() }} />
          {canWrite && (
            <Select
              placeholder={<span><PlusOutlined /> 新增記錄</span>}
              style={{ width: 220 }} value={null} showSearch optionFilterProp="label"
              popupMatchSelectWidth={320}
              options={groupedDefs}
              onChange={v => { const def = defs.find(d => d.id === v); if (def) openNew(def) }}
            />
          )}
        </Space>
      }
    >
      <Tabs
        activeKey={tab}
        onChange={k => { setTab(k as 'all' | 'pending'); setPage(1) }}
        items={[
          { key: 'all', label: '全部記錄' },
          {
            key: 'pending',
            label: <Badge count={pendingCount} size="small" offset={[8, -2]}>待我簽核</Badge>,
          },
        ]}
      />

      {tab === 'pending' && selectedIds.length > 0 && (
        <Alert type="info" showIcon style={{ marginBottom: 10 }}
          message={`已選 ${selectedIds.length} 筆`}
          action={
            <Popconfirm title={`批次同意 ${selectedIds.length} 筆記錄？`} onConfirm={batchApprove}>
              <Button size="small" type="primary" icon={<CheckOutlined />}>批次同意</Button>
            </Popconfirm>
          } />
      )}

      <Table<RecordRow>
        rowKey="id" loading={loading} dataSource={rows} size="middle" scroll={{ x: 900 }}
        rowSelection={tab === 'pending' ? {
          selectedRowKeys: selectedIds,
          onChange: keys => setSelectedIds(keys as number[]),
        } : undefined}
        pagination={{
          current: page, pageSize, total, showSizeChanger: true,
          showTotal: t => `共 ${t} 筆`,
          onChange: (p, ps) => { setPage(p); setPageSize(ps) },
        }}
        columns={[
          { title: '記錄日期', dataIndex: 'recordDate', width: 108 },
          { title: '表單', dataIndex: 'formName', width: 150 },
          { title: '狀態', key: 'status', width: 150, render: (_, row) => statusTag(row) },
          { title: '記錄者', dataIndex: 'createdBy', width: 96, render: v => v ?? '—' },
          {
            title: '操作', key: 'action', width: 200,
            render: (_, row) => (
              <Space>
                {row.status === 'Draft' && canWrite ? (
                  <>
                    <Button size="small" icon={<EditOutlined />} onClick={() => openExisting(row)}>編輯</Button>
                    <Popconfirm title="刪除此草稿？" onConfirm={() => remove(row)}>
                      <Button size="small" danger icon={<DeleteOutlined />} />
                    </Popconfirm>
                  </>
                ) : (
                  <Button size="small" icon={<EyeOutlined />} onClick={() => openExisting(row)}>
                    {tab === 'pending' ? '檢視簽核' : '檢視'}
                  </Button>
                )}
              </Space>
            ),
          },
        ]}
      />

      <Drawer
        title={
          <Space>
            {editorDef?.name}
            {record && statusTag(record)}
          </Space>
        }
        width={760} open={editorOpen} onClose={() => setEditorOpen(false)}
        extra={
          <Space>
            {!readOnly && (
              <>
                <Button loading={saving} onClick={() => save(false)}>儲存草稿</Button>
                <Popconfirm title="送簽後將鎖定內容進入簽核，確定？" onConfirm={() => save(true)}>
                  <Button type="primary" icon={<SendOutlined />} loading={saving}>儲存並送簽</Button>
                </Popconfirm>
              </>
            )}
            {record?.canWithdraw && record.isOwner && (
              <Popconfirm title="抽回此記錄？將回到草稿可再編輯" onConfirm={() => doWithdraw(record.id)}>
                <Button icon={<RollbackOutlined />}>抽單</Button>
              </Popconfirm>
            )}
            {record?.status === 'Submitted' && auth.hasPerm('record.cancel') && (
              <Button danger icon={<StopOutlined />} onClick={() => doCancel(record.id)}>撤銷</Button>
            )}
            {record?.canApprove && (
              <>
                <Button type="primary" icon={<CheckOutlined />} onClick={() => doApprove(record.id)}>
                  {record.approveOnBehalfOfName ? `代 ${record.approveOnBehalfOfName} 同意` : '同意'}
                </Button>
                <Button danger icon={<CloseOutlined />} onClick={() => doReject(record.id)}>退件</Button>
              </>
            )}
          </Space>
        }
      >
        {!schema ? <Spin /> : (
          <div>
            {record?.status === 'Cancelled' && (
              <Alert type="warning" showIcon style={{ marginBottom: 14 }}
                message="此記錄已撤銷" description={record.cancelReason} />
            )}

            {record?.wasRejected && record.status === 'Draft' && (
              <Alert type="error" showIcon style={{ marginBottom: 14 }}
                message="此記錄被退件"
                description={record.approvalSteps.filter(s => s.status === 'Rejected').slice(-1)
                  .map(s => `${s.name} ${s.actedByName ?? ''}：${s.comment ?? ''}`).join('')} />
            )}

            <Space wrap style={{ marginBottom: 18 }}>
              <span>
                <Typography.Text strong>記錄日期：</Typography.Text>
                <DatePicker value={recordDate} disabled={readOnly}
                  onChange={d => d && setRecordDate(d)} />
              </span>
              <span>
                <Typography.Text strong>標籤：</Typography.Text>
                <Select mode="tags" style={{ minWidth: 180 }} value={tags} disabled={readOnly}
                  placeholder="輸入後 Enter" onChange={v => setTags(v)} open={false} suffixIcon={null} />
              </span>
            </Space>

            {schema.sections.filter(section => visibleBy(section.showWhen, content)).map((section, si) => (
              <Card key={si} size="small" title={section.title} style={{ marginBottom: 14 }}>
                {section.help && (
                  <div style={{ whiteSpace: 'pre-line', fontSize: 'var(--fs-sm)', color: 'var(--c-text-muted)', background: 'var(--c-bg-subtle, #f6f7f9)', borderRadius: 6, padding: '6px 10px', marginBottom: 10, lineHeight: 1.7 }}>
                    {section.help}
                  </div>
                )}
                {section.type === 'rows' && section.key ? (
                  <RowsSection section={section} disabled={readOnly}
                    rows={(content[section.key] as RowData[] | undefined) ?? []}
                    onChange={rows => setContent({ ...content, [section.key!]: rows })} />
                ) : (
                  <Space direction="vertical" style={{ width: '100%' }} size={10}>
                    {(section.fields ?? []).map(field => (
                      <div key={field.key}>
                        <Typography.Text type="secondary" style={{ fontSize: 'var(--fs-sm)' }}>
                          {field.required && <span style={{ color: 'var(--c-danger)' }}>* </span>}{field.label}
                        </Typography.Text>
                        {field.help && (
                          <div style={{ whiteSpace: 'pre-line', fontSize: 'var(--fs-sm)', color: 'var(--c-text-muted)', background: 'var(--c-bg-subtle, #f6f7f9)', borderRadius: 6, padding: '6px 10px', margin: '4px 0 6px', lineHeight: 1.7 }}>
                            {field.help}
                          </div>
                        )}
                        {field.type === 'photos' ? (
                          <>
                            {/* 舊記錄這一欄是文字（檔名或編號）：照樣顯示，不讓它看起來像不見了 */}
                            {typeof content[field.key] === 'string' && (content[field.key] as string).trim() !== '' && (
                              <div style={{ fontSize: 'var(--fs-sm)', color: 'var(--c-text-muted)', margin: '2px 0 6px' }}>
                                原填寫：{content[field.key] as string}
                              </div>
                            )}
                            {!record ? (
                              <div style={{ fontSize: 'var(--fs-sm)', color: 'var(--c-text-muted)' }}>請先「儲存草稿」，之後才能上傳照片</div>
                            ) : (
                              <FileAttachments
                                category="Record"
                                ownerType={`work_record:${field.key}`}
                                ownerId={record.id}
                                writePermission="record.write"
                                readOnly={readOnly}
                                layout="gallery"
                                accept="image/*"
                                emptyText={readOnly ? '沒有上傳照片' : '尚未上傳照片（送簽前上傳；送簽後不能增刪）'}
                              />
                            )}
                          </>
                        ) : (
                          <FieldControl field={field} value={content[field.key]} disabled={readOnly || !!field.compute}
                            onChange={v => setContent({ ...content, [field.key]: v })} />
                        )}
                      </div>
                    ))}
                  </Space>
                )}
              </Card>
            ))}

            <Card size="small" title="附件" style={{ marginBottom: 14 }}>
              {/*
                ⚠ 附件與表單內容的存檔時機不同：附件是<b>上傳即生效</b>，不等「儲存草稿」。
                   所以尚未建立的記錄（沒有 id）不給上傳，避免使用者以為附件會跟著草稿一起送出。
                ⚠ 已送簽／已完成的記錄仍可<b>看</b>附件但不可增刪 ——
                   內容鎖定的單據，附件也是內容的一部分。
              */}
              {!record ? (
                <div style={{ fontSize: 'var(--fs-sm)', color: 'var(--c-text-muted)' }}>
                  請先「儲存草稿」，之後才能上傳附件
                </div>
              ) : (
                <FileAttachments
                  category="Record"
                  ownerType="work_record"
                  ownerId={record.id}
                  writePermission="record.write"
                  // 唯讀（已送簽、已完成、無編輯權）時只給看與下載
                  readOnly={readOnly}
                  layout="list"
                  maxSizeMb={20}
                  accept="image/*,.pdf,.doc,.docx,.xls,.xlsx"
                  emptyText={readOnly ? '此記錄沒有附件' : '尚無附件（可放照片、簽名掃描、相關文件）'}
                />
              )}
            </Card>

            {record && record.approvalSteps.length > 0 && (
              <Card size="small" title="簽核歷程" style={{ marginBottom: 14 }}>
                {approvalTimeline(record.approvalSteps)}
              </Card>
            )}
          </div>
        )}
      </Drawer>
    </Card>
  )
}
