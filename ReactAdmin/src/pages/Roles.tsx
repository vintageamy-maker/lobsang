import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  Card, Table, Button, Space, Modal, Form, Input, Tree, Checkbox,
  message, Tag, Popconfirm, Typography, Tooltip,
} from 'antd'
import { PlusOutlined, EditOutlined, DeleteOutlined, TeamOutlined } from '@ant-design/icons'
import { api, ApiError, type MenuNode } from '../api'

interface RoleRow {
  id: number
  name: string
  /** 標準角色的固定代碼（改名不影響系統判斷）；自建的角色是 null */
  code: string | null
  description: string | null
  permissionIds: number[]
  userCount: number
}

interface Permission { id: number; code: string; name: string; group: string | null }

/* 角色的使用者明細。清單頁只給得出一個數字，而「10 個人有這個權限」
   跟「哪 10 個人有」是兩件事 —— 要判斷改動角色的影響範圍，看的是後者。 */
interface RoleUser {
  id: number
  name: string
  account: string
  title: string | null
  orgUnitName: string | null
  isActive: boolean
  otherRoles: string[]
}
interface RoleUsers {
  roleId: number
  roleName: string
  total: number
  users: RoleUser[]
}

export default function Roles() {
  const [rows, setRows] = useState<RoleRow[]>([])
  const [loading, setLoading] = useState(false)
  const [permissions, setPermissions] = useState<Permission[]>([])
  const [menuTree, setMenuTree] = useState<MenuNode[]>([])
  const [editing, setEditing] = useState<RoleRow | 'new' | null>(null)
  const [checkedMenuPerms, setCheckedMenuPerms] = useState<number[]>([])
  const [checkedExtraPerms, setCheckedExtraPerms] = useState<number[]>([])
  const [form] = Form.useForm()
  const [members, setMembers] = useState<RoleUsers | null>(null)
  const [membersLoading, setMembersLoading] = useState(false)

  const openMembers = async (row: RoleRow) => {
    // 先用清單頁已有的數字開窗，讓 Modal 立刻有標題，再補明細
    setMembers({ roleId: row.id, roleName: row.name, total: row.userCount, users: [] })
    setMembersLoading(true)
    try {
      setMembers(await api.get<RoleUsers>(`/api/admin/roles/${row.id}/users`))
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '載入失敗')
      setMembers(null)
    } finally { setMembersLoading(false) }
  }

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setRows(await api.get<RoleRow[]>('/api/admin/roles'))
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '載入失敗')
    } finally { setLoading(false) }
  }, [])

  useEffect(() => {
    load()
    api.get<Permission[]>('/api/admin/permissions').then(setPermissions).catch(() => {})
    api.get<MenuNode[]>('/api/admin/menus').then(setMenuTree).catch(() => {})
  }, [load])

  const permByCode = useMemo(() => new Map(permissions.map(p => [p.code, p])), [permissions])

  // 選單樹上有掛權限碼的節點 → 對應權限 id；沒被任何選單涵蓋的權限（如 system.admin）另列
  const menuLinkedPermIds = useMemo(() => {
    const ids = new Set<number>()
    const walk = (nodes: MenuNode[]) => nodes.forEach(n => {
      const p = n.permissionCode ? permByCode.get(n.permissionCode) : undefined
      if (p) ids.add(p.id)
      walk(n.children)
    })
    walk(menuTree)
    return ids
  }, [menuTree, permByCode])

  const extraPerms = useMemo(
    () => permissions.filter(p => !menuLinkedPermIds.has(p.id)),
    [permissions, menuLinkedPermIds])

  interface TreeItem { title: string; key: string; checkable: boolean; children?: TreeItem[] }
  const treeData = useMemo(() => {
    const toNode = (n: MenuNode): TreeItem => {
      const p = n.permissionCode ? permByCode.get(n.permissionCode) : undefined
      return {
        title: p ? `${n.name}（${p.code}）` : n.name,
        key: p ? `perm-${p.id}` : `dir-${n.id}`,
        checkable: true,
        children: n.children.length ? n.children.map(toNode) : undefined,
      }
    }
    return menuTree.map(toNode)
  }, [menuTree, permByCode])

  const openEdit = (row: RoleRow | 'new') => {
    setEditing(row)
    if (row === 'new') {
      form.setFieldsValue({ name: '', description: '' })
      setCheckedMenuPerms([]); setCheckedExtraPerms([])
    } else {
      form.setFieldsValue({ name: row.name, description: row.description })
      setCheckedMenuPerms(row.permissionIds.filter(id => menuLinkedPermIds.has(id)))
      setCheckedExtraPerms(row.permissionIds.filter(id => !menuLinkedPermIds.has(id)))
    }
  }

  const save = async () => {
    const values = await form.validateFields()
    const permissionIds = [...new Set([...checkedMenuPerms, ...checkedExtraPerms])]
    try {
      if (editing === 'new') {
        await api.post('/api/admin/roles', { ...values, permissionIds })
        message.success('角色已建立')
      } else if (editing) {
        await api.put(`/api/admin/roles/${editing.id}`, { ...values, permissionIds })
        message.success('已更新')
      }
      setEditing(null)
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '儲存失敗')
    }
  }

  const remove = async (row: RoleRow) => {
    try {
      await api.del(`/api/admin/roles/${row.id}`)
      message.success('已刪除')
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '刪除失敗')
    }
  }

  return (
    <Card
      title="角色管理"
      extra={<Button type="primary" icon={<PlusOutlined />} onClick={() => openEdit('new')}>新增角色</Button>}
    >
      <Table<RoleRow>
        rowKey="id" loading={loading} dataSource={rows} size="middle" pagination={false} scroll={{ x: 640 }}
        columns={[
          {
            title: '角色名稱', dataIndex: 'name',
            // ⭐ 標準角色標「系統角色」：可以改名，系統認的是背後的代碼，改名不會讓舊角色長回來
            render: (v: string, r) => r.code
              ? <Space size={6}>{v}<Tooltip title={`系統角色（代碼 ${r.code}）：可以改名，改名不影響簽核與權限判斷`}><Tag>系統角色</Tag></Tooltip></Space>
              : v,
          },
          { title: '說明', dataIndex: 'description', render: v => v ?? '—' },
          {
            title: '權限數', dataIndex: 'permissionIds', width: 90,
            render: (ids: number[]) => <Tag>{ids.length}</Tag>,
          },
          {
            // 數字本身就是入口 —— 看到「10」的當下想知道的就是「哪 10 個」，
            // 再多一顆按鈕只是把同一個動作往旁邊挪。
            title: '使用人數', dataIndex: 'userCount', width: 100,
            render: (n: number, row) => n > 0
              ? (
                <Tooltip title="看看是哪些人">
                  <Button type="link" size="small" style={{ padding: 0 }}
                    onClick={() => openMembers(row)}>
                    <TeamOutlined /> {n} 人
                  </Button>
                </Tooltip>
              )
              : <span style={{ color: 'var(--c-text-muted)' }}>—</span>,
          },
          {
            title: '操作', key: 'action', width: 170,
            render: (_, row) => (
              <Space>
                <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(row)}>編輯</Button>
                <Popconfirm title={`確定刪除角色「${row.name}」？`} onConfirm={() => remove(row)}>
                  <Button size="small" danger icon={<DeleteOutlined />}>刪除</Button>
                </Popconfirm>
              </Space>
            ),
          },
        ]}
      />

      <Modal
        title={editing === 'new' ? '新增角色' : '編輯角色'} width={560}
        open={editing !== null} onOk={save} onCancel={() => setEditing(null)} destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 12 }}>
          <Form.Item name="name" label="角色名稱" rules={[{ required: true, message: '請輸入角色名稱' }]}>
            <Input placeholder="如：部門主管、行政人員" />
          </Form.Item>
          <Form.Item name="description" label="說明">
            <Input.TextArea rows={2} />
          </Form.Item>
        </Form>

        <Typography.Text strong>選單權限</Typography.Text>
        <div style={{ border: '1px solid var(--c-border)', borderRadius: 8, padding: 10, margin: '8px 0 16px' }}>
          <Tree
            checkable defaultExpandAll treeData={treeData}
            checkedKeys={checkedMenuPerms.map(id => `perm-${id}`)}
            onCheck={checked => {
              const keys = Array.isArray(checked) ? checked : checked.checked
              setCheckedMenuPerms(keys
                .filter(k => String(k).startsWith('perm-'))
                .map(k => Number(String(k).slice(5))))
            }}
          />
        </div>

        {extraPerms.length > 0 && (
          <>
            <Typography.Text strong>進階權限（未對應選單）</Typography.Text>
            <div style={{ border: '1px solid var(--c-border)', borderRadius: 8, padding: 10, marginTop: 8 }}>
              <Checkbox.Group
                value={checkedExtraPerms}
                onChange={v => setCheckedExtraPerms(v as number[])}
                options={extraPerms.map(p => ({
                  value: p.id,
                  label: `${p.name}（${p.code}）`,
                }))}
              />
            </div>
          </>
        )}
      </Modal>

      <Modal
        title={`使用「${members?.roleName ?? ''}」的人員（${members?.total ?? 0}）`}
        width={720} open={members !== null} footer={null}
        onCancel={() => setMembers(null)} destroyOnHidden
      >
        <Table<RoleUser>
          rowKey="id" size="small" loading={membersLoading}
          dataSource={members?.users ?? []}
          pagination={members && members.users.length > 12 ? { pageSize: 12 } : false}
          locale={{ emptyText: '沒有人使用這個角色' }}
          columns={[
            {
              title: '姓名', dataIndex: 'name', width: 130,
              render: (v: string, r) => (
                <Space size={4}>
                  <span>{v}</span>
                  {/* 停用者照樣列出並標示：濾掉會讓數字對不上清單頁，
                      而且復職時權限原封不動回來 —— 那正是調角色前要看到的事 */}
                  {!r.isActive && <Tag color="default">停用</Tag>}
                </Space>
              ),
            },
            { title: '帳號', dataIndex: 'account', width: 110 },
            { title: '職稱', dataIndex: 'title', width: 110, render: v => v ?? '—' },
            { title: '單位', dataIndex: 'orgUnitName', width: 120, render: v => v ?? '—' },
            {
              title: '兼掛的其他角色', dataIndex: 'otherRoles',
              render: (v: string[]) => v.length === 0
                ? <span style={{ color: 'var(--c-text-muted)' }}>無</span>
                : <Space size={4} wrap>{v.map(n => <Tag key={n}>{n}</Tag>)}</Space>,
            },
          ]}
        />
        <Typography.Paragraph type="secondary" style={{ fontSize: 'var(--fs-sm)', marginTop: 10, marginBottom: 0 }}>
          「兼掛的其他角色」是判斷影響範圍用的：權限取聯集，
          拿掉這個角色之後，這個人還會剩下那些角色帶來的權限。
        </Typography.Paragraph>
      </Modal>
    </Card>
  )
}
