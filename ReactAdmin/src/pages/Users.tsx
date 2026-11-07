import { useCallback, useEffect, useState } from 'react'
import {
  Card, Table, Button, Input, Select, Space, Switch, Modal, Form,
  message, Tag, Typography, Popconfirm, Row, Col, Radio, Tooltip,
} from 'antd'
import { PlusOutlined, ReloadOutlined, KeyOutlined, EditOutlined } from '@ant-design/icons'
import { DatePicker } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { api, ApiError } from '../api'
import Forbidden from '../components/Forbidden'
import DeactivationBlocker from '../components/DeactivationBlocker'

interface UserRow {
  id: number
  account: string
  name: string
  email: string | null
  isActive: boolean
  mustChangePassword: boolean
  lastLoginAt: string | null
  roleIds: number[]
  roles: string[]
  createdAt: string
  orgUnitId: number | null
  orgUnitName: string | null
  employeeNo: string | null
  title: string | null
  employmentType: string | null
  hireDate: string | null
  terminationDate: string | null
  terminationReason: string | null
  phone: string | null
  lineBound: boolean
  lineBoundAt: string | null
}

interface RoleOption { id: number; name: string }
interface OrgUnitOption { id: number; code: string; name: string; kind: string; parentId: number | null }

const EMPLOYMENT_TYPES = ['全職', '兼職', '約聘']
const ISO = 'YYYY-MM-DD'

/** 只送後端認得的欄位；避免把 orgUnitName、roles 等唯讀欄位一起 PUT 回去 */
const toPayload = (u: UserRow) => ({
  account: u.account, name: u.name, email: u.email, isActive: u.isActive, roleIds: u.roleIds,
  orgUnitId: u.orgUnitId, employeeNo: u.employeeNo, title: u.title,
  employmentType: u.employmentType, hireDate: u.hireDate,
  terminationDate: u.terminationDate, terminationReason: u.terminationReason, phone: u.phone,
})

/**
 * 表單的日期欄是 Dayjs，送出前轉回 yyyy-MM-dd。
 * 「狀態」只是表單上的開關（在職＝清空離職欄位），不是後端欄位 —— 送出前拿掉。
 */
const fromForm = (values: Record<string, unknown>) => {
  const resigned = values.employStatus === '離職'
  const { employStatus: _ignored, ...rest } = values
  return {
    ...rest,
    hireDate: values.hireDate ? (values.hireDate as Dayjs).format(ISO) : null,
    terminationDate: resigned && values.terminationDate
      ? (values.terminationDate as Dayjs).format(ISO) : null,
    terminationReason: resigned
      ? ((values.terminationReason as string | undefined)?.trim() || null) : null,
  }
}

export default function Users() {
  const [rows, setRows] = useState<UserRow[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [keyword, setKeyword] = useState('')
  const [roleFilter, setRoleFilter] = useState<number | undefined>()
  const [loading, setLoading] = useState(false)
  const [forbidden, setForbidden] = useState(false)
  const [roleOptions, setRoleOptions] = useState<RoleOption[]>([])
  const [orgOptions, setOrgOptions] = useState<OrgUnitOption[]>([])
  const [orgFilter, setOrgFilter] = useState<number | undefined>()
  const [editing, setEditing] = useState<UserRow | 'new' | null>(null)
  /** 停用被擋下時，要攤開來給人看的那個人 */
  const [blocking, setBlocking] = useState<{ id: number; name: string } | null>(null)
  const [form] = Form.useForm()
  // 狀態選「離職」才出現離職日（必填）與離職原因（選填）
  const employStatus = Form.useWatch<string>('employStatus', form)

  const load = useCallback(async (p = page, ps = pageSize) => {
    setLoading(true)
    try {
      const qs = new URLSearchParams({ pageNumber: String(p), pageSize: String(ps) })
      if (keyword.trim()) qs.set('keyword', keyword.trim())
      if (roleFilter) qs.set('roleId', String(roleFilter))
      if (orgFilter) qs.set('orgUnitId', String(orgFilter))
      const data = await api.get<{ items: UserRow[]; total: number }>(`/api/admin/users?${qs}`)
      setRows(data.items); setTotal(data.total)
    } catch (e) {
      // 403 不是「載入失敗」也不是「沒有資料」，是「你不該看這頁」—— 分開處理。
      // 只彈 toast 的話畫面留下一張空表格，使用者會以為是資料不見了。
      if (e instanceof ApiError && e.status === 403) { setForbidden(true); return }
      message.error(e instanceof ApiError ? e.message : '載入失敗')
    } finally { setLoading(false) }
  }, [page, pageSize, keyword, roleFilter, orgFilter])

  useEffect(() => { load() }, [load])
  useEffect(() => {
    api.get<RoleOption[]>('/api/admin/users/role-options').then(setRoleOptions).catch(() => {})
    api.get<OrgUnitOption[]>('/api/admin/users/org-unit-options').then(setOrgOptions).catch(() => {})
  }, [])

  const openEdit = (row: UserRow | 'new') => {
    setEditing(row)
    if (row === 'new') {
      form.setFieldsValue({
        account: '', name: '', email: '', isActive: true, roleIds: [],
        orgUnitId: undefined, employeeNo: '', title: '', employmentType: '全職',
        hireDate: null, employStatus: '在職', terminationDate: null, terminationReason: '', phone: '',
      })
    } else {
      form.setFieldsValue({
        account: row.account, name: row.name, email: row.email, isActive: row.isActive, roleIds: row.roleIds,
        orgUnitId: row.orgUnitId ?? undefined, employeeNo: row.employeeNo ?? '', title: row.title ?? '',
        employmentType: row.employmentType ?? undefined,
        hireDate: row.hireDate ? dayjs(row.hireDate) : null,
        employStatus: row.terminationDate ? '離職' : '在職',
        terminationDate: row.terminationDate ? dayjs(row.terminationDate) : null,
        terminationReason: row.terminationReason ?? '',
        phone: row.phone ?? '',
      })
    }
  }

  const save = async () => {
    const raw = await form.validateFields()
    const values = fromForm(raw)
    try {
      if (editing === 'new') {
        const res = await api.post<{ user: UserRow; initialPassword: string }>('/api/admin/users', values)
        Modal.success({
          title: '帳號已建立',
          content: (
            <div>
              <p>帳號 <b>{res.user.account}</b> 的初始密碼（僅顯示一次，請轉告使用者）：</p>
              <Typography.Paragraph copyable code style={{ fontSize: 'var(--fs-lg)' }}>{res.initialPassword}</Typography.Paragraph>
              <p style={{ color: 'var(--c-text-muted)', fontSize: 'var(--fs-sm)' }}>使用者首次登入將被要求變更密碼</p>
            </div>
          ),
        })
      } else if (editing) {
        await api.put(`/api/admin/users/${editing.id}`, values)
        message.success('已更新')
      }
      setEditing(null)
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '儲存失敗')
    }
  }

  const applyActive = async (row: UserRow, next: boolean) => {
    try {
      await api.put(`/api/admin/users/${row.id}`, { ...toPayload(row), isActive: next })
      message.success(next ? '已啟用' : '已停用')
      setBlocking(null)
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '操作失敗')
    }
  }

  /*
   * ⚠⚠ 停用之前先問「這個人身上還掛著什麼簽核」。
   *
   * 後端本來就會擋（PUT 會回 400），這裡先問一次是為了**把清單攤開給人看**——
   * 只丟一句「不能停用」，操作的人不知道要去清什麼。
   * ⭐ 真正的把關在後端，這一層只是為了讓人做得完。
   */
  const toggleActive = async (row: UserRow, next: boolean) => {
    if (next) { await applyActive(row, true); return }
    try {
      const impact = await api.get<{ blocked: boolean }>(
        `/api/admin/users/${row.id}/deactivation-impact`)
      if (impact.blocked) { setBlocking({ id: row.id, name: row.name }); return }
    } catch {
      // 查不到影響就交給後端擋 —— 不要因為查詢失敗就讓人停用不了
    }
    await applyActive(row, false)
  }

  /**
   * 解除某人的 LINE 綁定。
   * ⚠ 會要求填原因 —— 綁定是身分變更，解除也是，稽核軌跡上要看得出為什麼。
   */
  const unbindLine = async (row: UserRow) => {
    const reason = window.prompt(`解除「${row.name}」的 LINE 綁定，原因：`, '手機遺失／換裝置')
    if (reason === null) return   // 按取消
    try {
      await api.post(`/api/admin/users/${row.id}/unbind-line`, { reason })
      message.success(`已解除「${row.name}」的 LINE 綁定`)
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '解除失敗')
    }
  }

  const resetPassword = async (row: UserRow) => {
    try {
      const res = await api.post<{ newPassword: string }>(`/api/admin/users/${row.id}/reset-password`)
      Modal.success({
        title: `已重設「${row.name}」的密碼`,
        content: (
          <div>
            <p>新密碼（僅顯示一次，請轉告使用者）：</p>
            <Typography.Paragraph copyable code style={{ fontSize: 'var(--fs-lg)' }}>{res.newPassword}</Typography.Paragraph>
          </div>
        ),
      })
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '重設失敗')
    }
  }

  if (forbidden) return <Card><Forbidden what="帳號管理" /></Card>

  return (
    <Card
      title="帳號管理"
      extra={
        <Space wrap>
          <Input.Search allowClear placeholder="帳號 / 姓名 / 員工編號" style={{ width: 200 }}
            onSearch={v => { setKeyword(v); setPage(1) }} />
          <Select allowClear placeholder="單位" style={{ width: 140 }}
            options={orgOptions.map(o => ({ value: o.id, label: `${o.code} ${o.name}` }))}
            onChange={v => { setOrgFilter(v); setPage(1) }} />
          <Select allowClear placeholder="角色" style={{ width: 140 }}
            options={roleOptions.map(r => ({ value: r.id, label: r.name }))}
            onChange={v => { setRoleFilter(v); setPage(1) }} />
          <Button icon={<ReloadOutlined />} onClick={() => load()} />
          <Button type="primary" icon={<PlusOutlined />} onClick={() => openEdit('new')}>新增帳號</Button>
        </Space>
      }
    >
      <Table<UserRow>
        rowKey="id" loading={loading} dataSource={rows} size="middle" scroll={{ x: 1200 }}
        pagination={{
          current: page, pageSize, total, showSizeChanger: true,
          showTotal: t => `共 ${t} 筆`,
          onChange: (p, ps) => { setPage(p); setPageSize(ps) },
        }}
        columns={[
          { title: '帳號', dataIndex: 'account', width: 110 },
          { title: '姓名', dataIndex: 'name', width: 90 },
          { title: '員工編號', dataIndex: 'employeeNo', width: 100, render: v => v ?? '—' },
          {
            title: '單位', dataIndex: 'orgUnitName', width: 100,
            render: v => v ? <Tag color="blue">{v}</Tag> : '—',
          },
          { title: '職稱', dataIndex: 'title', width: 100, render: v => v ?? '—' },
          {
            title: '到職日', dataIndex: 'hireDate', width: 110,
            render: (v, row) => (
              <Space direction="vertical" size={0}>
                {v ? dayjs(v).format('YYYY/MM/DD') : <Tag color="warning">未設定</Tag>}
                {row.terminationDate && (
                  <Tooltip title={row.terminationReason ?? undefined}>
                    <Tag color="error" style={{ margin: 0 }}>
                      {dayjs(row.terminationDate).format('YYYY/MM/DD')} 離職
                    </Tag>
                  </Tooltip>
                )}
              </Space>
            ),
          },
          {
            title: '角色', dataIndex: 'roles', width: 140,
            render: (roles: string[]) => <Space wrap size={4}>{roles.map(r => <Tag key={r} style={{ margin: 0 }}>{r}</Tag>)}</Space>,
          },
          {
            title: '預設密碼', dataIndex: 'mustChangePassword', width: 90,
            render: v => (v ? <Tag color="warning">未變更</Tag> : <Tag color="success">已變更</Tag>),
          },
          {
            title: '狀態', dataIndex: 'isActive', width: 80,
            render: (v: boolean, row) => (
              <Switch checked={v} size="small" disabled={row.account === 'admin'}
                onChange={next => toggleActive(row, next)} />
            ),
          },
          {
            /*
             * ⚠ 這一欄的用途不只是資訊，是**看得見異常**：
             *   知道他人密碼者可以把自己的 LINE 綁到別人帳號上，
             *   而管理員若看不到誰綁了 LINE，就永遠不會發現。
             * ⚠ 只顯示「有沒有綁」不顯示 LINE userId —— 那是可對應到個人 LINE 的識別碼。
             */
            title: 'LINE', dataIndex: 'lineBound', width: 100,
            render: (v: boolean, row) => v
              ? <Tag color="green" title={row.lineBoundAt
                  ? `綁定於 ${new Date(row.lineBoundAt + 'Z').toLocaleString('zh-TW')}` : undefined}>已綁定</Tag>
              : <span style={{ color: 'var(--c-text-muted)' }}>—</span>,
          },
          {
            title: '最後登入', dataIndex: 'lastLoginAt', width: 160,
            render: v => (v ? new Date(v + 'Z').toLocaleString('zh-TW') : '—'),
          },
          {
            title: '操作', key: 'action', width: 290,
            render: (_, row) => (
              <Space>
                <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(row)}>編輯</Button>
                <Popconfirm title={`確定重設「${row.name}」的密碼？`} onConfirm={() => resetPassword(row)}>
                  <Button size="small" icon={<KeyOutlined />}>重設密碼</Button>
                </Popconfirm>
                {/*
                  * ⚠ 只在已綁定時出現 —— 沒綁的人放一顆按不了的鈕只是雜訊。
                  *
                  * 這條路是**必要的補救手段**，不只是方便：手機遺失、綁錯人、離職時，
                  * 本人自助都解不掉。尤其「知道他人密碼者把自己的 LINE 綁上去」這種情況，
                  * **改密碼也不會讓綁定失效**，只有這裡救得回來。
                  */}
                {row.lineBound && (
                  <Popconfirm
                    title={`解除「${row.name}」的 LINE 綁定？`}
                    description="解除後該員需重新綁定才能使用 LINE 功能。"
                    okText="解除" cancelText="取消" okButtonProps={{ danger: true }}
                    onConfirm={() => unbindLine(row)}>
                    <Button size="small" danger>解除 LINE</Button>
                  </Popconfirm>
                )}
              </Space>
            ),
          },
        ]}
      />

      <Modal
        title={editing === 'new' ? '新增帳號' : '編輯帳號'} width={600}
        open={editing !== null} onOk={save} onCancel={() => setEditing(null)} destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 12 }}>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="account" label="帳號" rules={[{ required: true, message: '請輸入帳號' }]}>
                <Input disabled={editing !== 'new'} placeholder="登入帳號" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="name" label="姓名" rules={[{ required: true, message: '請輸入姓名' }]}>
                <Input />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="employeeNo" label="員工編號">
                <Input placeholder="EMP-001" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="title" label="職稱">
                <Input placeholder="專員、主任…" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="orgUnitId" label="所屬單位" extra="簽核鏈的起點">
                <Select allowClear showSearch optionFilterProp="label" placeholder="選擇單位"
                  options={orgOptions.map(o => ({ value: o.id, label: `${o.code} ${o.name}` }))} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="employmentType" label="雇用類別">
                <Select allowClear options={EMPLOYMENT_TYPES.map(t => ({ value: t, label: t }))} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="hireDate" label="到職日">
                <DatePicker style={{ width: '100%' }} placeholder="選擇到職日" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="phone" label="聯絡電話">
                <Input maxLength={50} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="employStatus" label="狀態"
                extra="能否登入由「啟用」決定，確定離職時請一併關閉啟用">
                <Radio.Group options={[{ value: '在職', label: '在職' }, { value: '離職', label: '離職' }]} />
              </Form.Item>
            </Col>
            {employStatus === '離職' && (
              <>
                <Col span={12}>
                  <Form.Item name="terminationDate" label="離職日期"
                    rules={[{ required: true, message: '請選擇離職日期' }]}>
                    <DatePicker style={{ width: '100%' }} placeholder="選擇離職日期" />
                  </Form.Item>
                </Col>
                <Col span={12}>
                  <Form.Item name="terminationReason" label="離職原因（選填）">
                    <Input placeholder="自願離職、契約期滿…" maxLength={200} />
                  </Form.Item>
                </Col>
              </>
            )}
          </Row>

          <Form.Item name="email" label="Email">
            <Input type="email" />
          </Form.Item>
          <Form.Item name="roleIds" label="角色" rules={[{ required: true, message: '請選擇至少一個角色' }]}>
            <Select mode="multiple" options={roleOptions.map(r => ({ value: r.id, label: r.name }))} />
          </Form.Item>
          <Form.Item name="isActive" label="狀態" valuePropName="checked">
            <Switch checkedChildren="啟用" unCheckedChildren="停用" />
          </Form.Item>
        </Form>
      </Modal>

      <DeactivationBlocker
        user={blocking}
        onCancel={() => setBlocking(null)}
        onCleared={() => {
          const row = rows.find(r => r.id === blocking?.id)
          if (row) applyActive(row, false)
        }}
      />
    </Card>
  )
}
