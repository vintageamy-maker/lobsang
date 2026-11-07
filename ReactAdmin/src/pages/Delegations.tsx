import { useCallback, useEffect, useState } from 'react'
import {
  Card, Table, Button, Space, Modal, Form, Select, DatePicker, Input, Switch,
  message, Tag, Popconfirm, Typography,
} from 'antd'
import { PlusOutlined, ReloadOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons'
import dayjs, { type Dayjs } from 'dayjs'
import { api, ApiError } from '../api'
import GuideAlert from '../components/GuideAlert'

interface DelegationRow {
  id: number
  principalUserId: number
  principalName: string
  delegateUserId: number
  delegateName: string
  startDate: string
  endDate: string
  reason: string | null
  isActive: boolean
  inEffect: boolean
}

interface UserOption { id: number; name: string; account: string; title: string | null }

export default function Delegations() {
  const [rows, setRows] = useState<DelegationRow[]>([])
  const [loading, setLoading] = useState(false)
  const [users, setUsers] = useState<UserOption[]>([])
  const [editing, setEditing] = useState<DelegationRow | 'new' | null>(null)
  const [form] = Form.useForm()

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setRows(await api.get<DelegationRow[]>('/api/admin/delegations'))
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '載入失敗')
    } finally { setLoading(false) }
  }, [])

  useEffect(() => { load() }, [load])
  useEffect(() => { api.get<UserOption[]>('/api/admin/org-units/user-options').then(setUsers).catch(() => {}) }, [])

  const openEdit = (row: DelegationRow | 'new') => {
    setEditing(row)
    if (row === 'new') {
      form.setFieldsValue({
        principalUserId: undefined, delegateUserId: undefined,
        range: [dayjs(), dayjs().add(7, 'day')], reason: '', isActive: true,
      })
    } else {
      form.setFieldsValue({
        principalUserId: row.principalUserId, delegateUserId: row.delegateUserId,
        range: [dayjs(row.startDate), dayjs(row.endDate)],
        reason: row.reason ?? '', isActive: row.isActive,
      })
    }
  }

  const save = async () => {
    const v = await form.validateFields()
    const [start, end] = v.range as [Dayjs, Dayjs]
    const body = {
      principalUserId: v.principalUserId,
      delegateUserId: v.delegateUserId,
      startDate: start.format('YYYY-MM-DD'),
      endDate: end.format('YYYY-MM-DD'),
      reason: v.reason?.trim() ? v.reason.trim() : null,
      isActive: v.isActive,
    }
    try {
      if (editing === 'new') {
        await api.post('/api/admin/delegations', body)
        message.success('代理設定已建立')
      } else if (editing) {
        await api.put(`/api/admin/delegations/${editing.id}`, body)
        message.success('已更新')
      }
      setEditing(null)
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '儲存失敗')
    }
  }

  const remove = async (row: DelegationRow) => {
    try {
      await api.del(`/api/admin/delegations/${row.id}`)
      message.success('已刪除')
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '刪除失敗')
    }
  }

  const userLabel = (u: UserOption) => `${u.name}${u.title ? `（${u.title}）` : ''}`

  return (
    <Card
      title="代理設定"
      extra={
        <Space>
          <Button icon={<ReloadOutlined />} onClick={load} />
          <Button type="primary" icon={<PlusOutlined />} onClick={() => openEdit('new')}>新增代理</Button>
        </Space>
      }
    >
      <GuideAlert id="delegations-intro" style={{ marginBottom: 14 }}
        title="代理期間內，代理人可代被代理人簽核，所有代簽都會記錄「代誰簽」">
        敏感權限不會被繼承。代理人需重新登入才會取得代理權限。
      </GuideAlert>

      <Table<DelegationRow>
        rowKey="id" loading={loading} dataSource={rows} size="middle" pagination={false} scroll={{ x: 800 }}
        columns={[
          { title: '被代理人', dataIndex: 'principalName', width: 120 },
          {
            title: '代理人', dataIndex: 'delegateName', width: 120,
            render: v => <Tag color="blue">{v}</Tag>,
          },
          {
            title: '代理期間', key: 'range', width: 200,
            render: (_, row) => `${row.startDate} ~ ${row.endDate}`,
          },
          { title: '事由', dataIndex: 'reason', render: v => v ?? '—' },
          {
            title: '狀態', key: 'status', width: 100,
            render: (_, row) => row.inEffect
              ? <Tag color="success">生效中</Tag>
              : row.isActive ? <Tag>未在期間</Tag> : <Tag color="default">已停用</Tag>,
          },
          {
            title: '操作', key: 'action', width: 150,
            render: (_, row) => (
              <Space>
                <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(row)}>編輯</Button>
                <Popconfirm title="確定刪除此代理設定？" onConfirm={() => remove(row)}>
                  <Button size="small" danger icon={<DeleteOutlined />} />
                </Popconfirm>
              </Space>
            ),
          },
        ]}
      />

      <Modal
        title={editing === 'new' ? '新增代理設定' : '編輯代理設定'} width={520}
        open={editing !== null} onOk={save} onCancel={() => setEditing(null)} destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 12 }}>
          <Form.Item name="principalUserId" label="被代理人（權限的原主）"
            rules={[{ required: true, message: '請選擇被代理人' }]}>
            <Select showSearch optionFilterProp="label"
              options={users.map(u => ({ value: u.id, label: userLabel(u) }))} />
          </Form.Item>
          <Form.Item name="delegateUserId" label="代理人"
            rules={[{ required: true, message: '請選擇代理人' }]}>
            <Select showSearch optionFilterProp="label"
              options={users.map(u => ({ value: u.id, label: userLabel(u) }))} />
          </Form.Item>
          <Form.Item name="range" label="代理期間" rules={[{ required: true, message: '請選擇期間' }]}>
            <DatePicker.RangePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="reason" label="事由">
            <Input placeholder="休假、出差…" maxLength={200} />
          </Form.Item>
          <Form.Item name="isActive" label="啟用" valuePropName="checked">
            <Switch checkedChildren="啟用" unCheckedChildren="停用" />
          </Form.Item>
        </Form>
        <Typography.Text type="secondary" style={{ fontSize: 'var(--fs-sm)' }}>
          同一被代理人於同期間僅能有一位有效代理人。
        </Typography.Text>
      </Modal>
    </Card>
  )
}
