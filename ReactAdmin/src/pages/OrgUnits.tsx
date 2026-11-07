import { useCallback, useEffect, useState } from 'react'
import {
  Card, Table, Button, Space, Modal, Form, Input, Select, Switch, AutoComplete,
  InputNumber, message, Tag, Popconfirm, Row, Col, Typography,
} from 'antd'
import { PlusOutlined, ReloadOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons'
import { api, ApiError } from '../api'

interface OrgUnitNode {
  id: number
  code: string
  name: string
  kind: string
  managerUserId: number | null
  managerName: string | null
  sortOrder: number
  isActive: boolean
  userCount: number
  children: OrgUnitNode[]
}

interface UserOption { id: number; name: string; account: string; title: string | null }

/** 建議的層級名稱（可自由輸入其他名稱） */
const KINDS = ['機構', '部門', '組']
const KIND_COLOR: Record<string, string> = { 機構: 'purple', 部門: 'blue', 組: 'green' }

/** 樹攤平（含深度），供上級單位下拉的縮排顯示 */
function flatten(nodes: OrgUnitNode[], depth = 0): { node: OrgUnitNode; depth: number }[] {
  return nodes.flatMap(n => [{ node: n, depth }, ...flatten(n.children, depth + 1)])
}

export default function OrgUnits() {
  const [tree, setTree] = useState<OrgUnitNode[]>([])
  const [loading, setLoading] = useState(false)
  const [userOptions, setUserOptions] = useState<UserOption[]>([])
  const [editing, setEditing] = useState<OrgUnitNode | 'new' | null>(null)
  // 樹是非同步載入的，defaultExpandAllRows 對後到的資料無效，改為受控展開
  const [expandedKeys, setExpandedKeys] = useState<number[]>([])
  const [form] = Form.useForm()

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await api.get<OrgUnitNode[]>('/api/admin/org-units')
      setTree(data)
      setExpandedKeys(flatten(data).filter(f => f.node.children.length).map(f => f.node.id))
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '載入失敗')
    } finally { setLoading(false) }
  }, [])

  useEffect(() => { load() }, [load])
  useEffect(() => { api.get<UserOption[]>('/api/admin/org-units/user-options').then(setUserOptions).catch(() => {}) }, [])

  const flat = flatten(tree)

  const openEdit = (row: OrgUnitNode | 'new') => {
    setEditing(row)
    if (row === 'new') {
      form.setFieldsValue({
        code: '', name: '', kind: '部門', parentId: undefined, managerUserId: undefined,
        sortOrder: 0, isActive: true,
      })
    } else {
      const parent = flat.find(f => f.node.children.some(c => c.id === row.id))?.node
      form.setFieldsValue({
        code: row.code, name: row.name, kind: row.kind, parentId: parent?.id,
        managerUserId: row.managerUserId ?? undefined, sortOrder: row.sortOrder,
        isActive: row.isActive,
      })
    }
  }

  const save = async () => {
    const body = await form.validateFields()
    try {
      if (editing === 'new') {
        await api.post('/api/admin/org-units', body)
        message.success('已新增單位')
      } else if (editing) {
        await api.put(`/api/admin/org-units/${editing.id}`, body)
        message.success('已更新')
      }
      setEditing(null)
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '儲存失敗')
    }
  }

  const remove = async (row: OrgUnitNode) => {
    try {
      await api.del(`/api/admin/org-units/${row.id}`)
      message.success('已刪除')
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '刪除失敗')
    }
  }

  return (
    <Card
      title="組織編制"
      extra={
        <Space>
          <Button icon={<ReloadOutlined />} onClick={load} />
          <Button type="primary" icon={<PlusOutlined />} onClick={() => openEdit('new')}>新增單位</Button>
        </Space>
      }
    >
      <Typography.Paragraph type="secondary" style={{ marginTop: -8, fontSize: 'var(--fs-sm)' }}>
        組織編制是簽核鏈與資料可視範圍的依據：表單送簽時依此逐級往上找主管，單位主管的查詢範圍亦由此推導。
      </Typography.Paragraph>

      <Table<OrgUnitNode>
        rowKey="id" loading={loading} dataSource={tree} size="middle" pagination={false}
        scroll={{ x: 760 }}
        expandable={{
          expandedRowKeys: expandedKeys,
          onExpandedRowsChange: keys => setExpandedKeys(keys as number[]),
        }}
        columns={[
          { title: '單位名稱', dataIndex: 'name', width: 220 },
          { title: '代碼', dataIndex: 'code', width: 110, render: v => <Tag>{v}</Tag> },
          {
            title: '層級', dataIndex: 'kind', width: 80,
            render: v => <Tag color={KIND_COLOR[v] ?? 'default'}>{v}</Tag>,
          },
          {
            title: '單位主管', dataIndex: 'managerName', width: 120,
            render: v => v ?? <span style={{ color: 'var(--c-border-strong)' }}>未設定</span>,
          },
          {
            title: '人員數', dataIndex: 'userCount', width: 80,
            render: v => (v > 0 ? `${v} 人` : '—'),
          },
          { title: '排序', dataIndex: 'sortOrder', width: 70 },
          {
            title: '狀態', dataIndex: 'isActive', width: 80,
            render: v => (v ? <Tag color="success">啟用</Tag> : <Tag>停用</Tag>),
          },
          {
            title: '操作', key: 'action', width: 150,
            render: (_, row) => (
              <Space>
                <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(row)}>編輯</Button>
                <Popconfirm title={`確定刪除「${row.name}」？`} onConfirm={() => remove(row)}>
                  <Button size="small" danger icon={<DeleteOutlined />} />
                </Popconfirm>
              </Space>
            ),
          },
        ]}
      />

      <Modal
        title={editing === 'new' ? '新增單位' : '編輯單位'} width={600}
        open={editing !== null} onOk={save} onCancel={() => setEditing(null)} destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 12 }}>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="code" label="單位代碼" rules={[{ required: true, message: '請輸入代碼' }]}>
                <Input placeholder="HQ、IT…" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="name" label="單位名稱" rules={[{ required: true, message: '請輸入名稱' }]}>
                <Input placeholder="資訊部、行政組…" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="kind" label="層級" rules={[{ required: true, message: '請輸入層級' }]}
                extra="可選建議值或自行輸入">
                <AutoComplete options={KINDS.map(k => ({ value: k }))} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="parentId" label="上級單位">
                <Select allowClear placeholder="無（頂層）"
                  options={flat
                    .filter(f => editing === 'new' || f.node.id !== editing?.id)
                    .map(f => ({ value: f.node.id, label: `${'　'.repeat(f.depth)}${f.node.code} ${f.node.name}` }))} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="managerUserId" label="單位主管"
                extra="簽核鏈的下一關；未設定時往上一層單位找">
                <Select allowClear showSearch optionFilterProp="label"
                  options={userOptions.map(u => ({
                    value: u.id,
                    label: `${u.name}${u.title ? `（${u.title}）` : ''}`,
                  }))} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="sortOrder" label="排序" extra="0＝自動接在同層最後">
                <InputNumber min={0} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="isActive" label="狀態" valuePropName="checked">
            <Switch checkedChildren="啟用" unCheckedChildren="停用" />
          </Form.Item>

        </Form>
      </Modal>
    </Card>
  )
}
