import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  Card, Button, Space, Modal, Form, Input, Select, TreeSelect, Switch,
  message, Tag, Popconfirm, Alert, Spin, Typography, Tooltip,
} from 'antd'
import { PlusOutlined, EditOutlined, DeleteOutlined, HolderOutlined } from '@ant-design/icons'
import {
  DndContext, KeyboardSensor, PointerSensor, closestCenter,
  useSensor, useSensors, type DragEndEvent,
} from '@dnd-kit/core'
import { restrictToVerticalAxis } from '@dnd-kit/modifiers'
import {
  SortableContext, arrayMove, sortableKeyboardCoordinates,
  useSortable, verticalListSortingStrategy,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { api, ApiError, type MenuNode } from '../api'
import { ICON_NAMES, iconOf } from '../icons'

interface Permission { id: number; code: string; name: string; group: string | null }

// ===== 樹工具（同層拖曳；參考 meekee 後台模式）=====

function flatten(nodes: MenuNode[]): MenuNode[] {
  return nodes.flatMap(n => [n, ...flatten(n.children)])
}

function descendantIds(nodeId: number, all: MenuNode[]): Set<number> {
  const result = new Set<number>()
  const node = all.find(n => n.id === nodeId)
  if (!node) return result
  for (const child of node.children) {
    result.add(child.id)
    for (const id of descendantIds(child.id, all)) result.add(id)
  }
  return result
}

// 找出節點所屬兄弟群的 parentId（root 為 null）；找不到回傳 undefined
function groupParentId(nodes: MenuNode[], id: number, parentId: number | null = null): number | null | undefined {
  for (const node of nodes) {
    if (node.id === id) return parentId
    const found = groupParentId(node.children, id, node.id)
    if (found !== undefined) return found
  }
  return undefined
}

function moveById(list: MenuNode[], activeId: number, overId: number): MenuNode[] {
  const from = list.findIndex(n => n.id === activeId)
  const to = list.findIndex(n => n.id === overId)
  if (from === -1 || to === -1) return list
  return arrayMove(list, from, to)
}

// 只重排 parentId 底下的兄弟群
function reorderSiblingGroup(nodes: MenuNode[], parentId: number | null, activeId: number, overId: number): MenuNode[] {
  if (parentId === null) return moveById(nodes, activeId, overId)
  return nodes.map(node =>
    node.id === parentId
      ? { ...node, children: moveById(node.children, activeId, overId) }
      : { ...node, children: reorderSiblingGroup(node.children, parentId, activeId, overId) })
}

// 收集每一個兄弟群（root 群 + 每個有子節點的群）
function collectGroups(
  nodes: MenuNode[], parentId: number | null = null,
  acc: { parentId: number | null; ids: number[] }[] = [],
): { parentId: number | null; ids: number[] }[] {
  acc.push({ parentId, ids: nodes.map(n => n.id) })
  for (const node of nodes)
    if (node.children.length > 0) collectGroups(node.children, node.id, acc)
  return acc
}

// ===== 可拖曳選單列 =====

interface RowCtx {
  isDirty: boolean
  onEdit: (node: MenuNode) => void
  onAddChild: (node: MenuNode) => void
  onDelete: (node: MenuNode) => void
}

function MenuRow({ node, depth, ctx }: { node: MenuNode; depth: number; ctx: RowCtx }) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: node.id })
  const style: React.CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : undefined,
    position: isDragging ? 'relative' : undefined,
    zIndex: isDragging ? 1 : undefined,
  }

  return (
    <li ref={setNodeRef} style={style}>
      <div className="menu-row" style={{ paddingLeft: depth * 26 }}>
        <button type="button" className="menu-drag-handle" aria-label="拖曳排序" {...attributes} {...listeners}>
          <HolderOutlined />
        </button>
        <span className="menu-row-name">
          {node.icon && <span style={{ color: 'var(--c-text-muted)', marginRight: 6 }}>{iconOf(node.icon)}</span>}
          {node.name}
        </span>
        <span className="menu-row-meta">
          {node.path ? <code>{node.path}</code> : <Tag style={{ margin: 0 }}>目錄</Tag>}
        </span>
        <span className="menu-row-meta menu-row-perm">
          {node.permissionCode && <Tag color="orange" style={{ margin: 0 }}>{node.permissionCode}</Tag>}
        </span>
        {!node.isActive && <Tag style={{ margin: 0 }}>停用</Tag>}
        <span className="menu-row-actions">
          <Space size={4} wrap>
            <Button size="small" icon={<EditOutlined />} disabled={ctx.isDirty} onClick={() => ctx.onEdit(node)}>編輯</Button>
            <Tooltip title={node.path ? '功能頁不能新增子選單；請先清空路徑改為目錄' : undefined}>
              <Button size="small" icon={<PlusOutlined />} disabled={ctx.isDirty || Boolean(node.path)}
                onClick={() => ctx.onAddChild(node)}>子選單</Button>
            </Tooltip>
            <Popconfirm title={`確定刪除選單「${node.name}」？`} onConfirm={() => ctx.onDelete(node)}>
              <Button size="small" danger icon={<DeleteOutlined />} disabled={ctx.isDirty}>刪除</Button>
            </Popconfirm>
          </Space>
        </span>
      </div>
      {node.children.length > 0 && (
        <ul className="menu-children">
          <SortableContext items={node.children.map(c => c.id)} strategy={verticalListSortingStrategy}>
            {node.children.map(child => <MenuRow key={child.id} node={child} depth={depth + 1} ctx={ctx} />)}
          </SortableContext>
        </ul>
      )}
    </li>
  )
}

// ===== 主頁 =====

interface EditingMeta { id: number | null; hasChildren: boolean }

export default function Menus() {
  const [tree, setTree] = useState<MenuNode[]>([])
  const [workingTree, setWorkingTree] = useState<MenuNode[] | null>(null)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [permissions, setPermissions] = useState<Permission[]>([])
  const [editing, setEditing] = useState<EditingMeta | null>(null)
  const [form] = Form.useForm()

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setTree(await api.get<MenuNode[]>('/api/admin/menus'))
      setWorkingTree(null)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '載入失敗')
    } finally { setLoading(false) }
  }, [])

  useEffect(() => {
    load()
    api.get<Permission[]>('/api/admin/permissions').then(setPermissions).catch(() => {})
  }, [load])

  const displayTree = workingTree ?? tree
  const isDirty = workingTree !== null
  const allNodes = useMemo(() => flatten(displayTree), [displayTree])
  const excludedParentIds = editing?.id != null ? descendantIds(editing.id, allNodes) : new Set<number>()

  interface ParentOption { title: string; value: number; disabled?: boolean; children?: ParentOption[] }
  const parentOptions = useMemo(() => {
    const toNode = (n: MenuNode): ParentOption => ({
      title: n.name,
      value: n.id,
      // 不可選：自己/子孫（避免循環）、功能頁（有路徑者不能收子選單）
      disabled: n.id === editing?.id || excludedParentIds.has(n.id) || Boolean(n.path),
      children: n.children.length ? n.children.map(toNode) : undefined,
    })
    return displayTree.map(toNode)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [displayTree, editing])

  const openCreate = (parentId: number | null) => {
    setEditing({ id: null, hasChildren: false })
    form.setFieldsValue({ name: '', parentId: parentId ?? undefined, path: '', icon: undefined, permissionCode: undefined, isActive: true })
  }

  const openEdit = (node: MenuNode) => {
    const hasChildren = node.children.length > 0
    setEditing({ id: node.id, hasChildren })
    form.setFieldsValue({
      name: node.name,
      parentId: groupParentId(displayTree, node.id) ?? undefined,
      path: hasChildren ? '' : node.path ?? '',
      icon: node.icon ?? undefined,
      permissionCode: node.permissionCode ?? undefined,
      isActive: node.isActive,
    })
  }

  const save = async () => {
    const values = await form.validateFields()
    const existing = editing?.id != null ? allNodes.find(n => n.id === editing.id) : null
    const body = {
      name: values.name,
      parentId: values.parentId ?? null,
      path: editing?.hasChildren || !values.path?.trim() ? null : values.path.trim(),
      icon: values.icon ?? null,
      sortOrder: existing?.sortOrder ?? 0, // 新增=0 由後端接在同層最後；排序以拖曳維護
      permissionCode: values.permissionCode ?? null,
      isActive: values.isActive,
    }
    try {
      if (editing?.id == null) {
        await api.post('/api/admin/menus', body)
        message.success('選單已新增，請至角色管理勾選開放對象')
      } else {
        await api.put(`/api/admin/menus/${editing.id}`, body)
        message.success('已更新')
      }
      setEditing(null)
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '儲存失敗')
    }
  }

  const remove = async (node: MenuNode) => {
    try {
      await api.del(`/api/admin/menus/${node.id}`)
      message.success('已刪除')
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '刪除失敗')
    }
  }

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event
    if (!over || active.id === over.id) return
    const activeId = Number(active.id)
    const overId = Number(over.id)
    const activeParent = groupParentId(displayTree, activeId)
    const overParent = groupParentId(displayTree, overId)
    // 僅允許同層兄弟間拖曳；跨層放置忽略
    if (activeParent === undefined || activeParent !== overParent) return
    setWorkingTree(reorderSiblingGroup(displayTree, activeParent, activeId, overId))
  }

  const confirmReorder = async () => {
    setSaving(true)
    try {
      const serverGroups = new Map(collectGroups(tree).map(g => [String(g.parentId), g.ids.join(',')]))
      const changed = collectGroups(displayTree).filter(g => serverGroups.get(String(g.parentId)) !== g.ids.join(','))
      for (const group of changed)
        await api.put('/api/admin/menus/reorder', { parentId: group.parentId, ids: group.ids })
      message.success('排序已儲存')
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '儲存排序失敗')
    } finally { setSaving(false) }
  }

  const rowCtx: RowCtx = {
    isDirty,
    onEdit: openEdit,
    onAddChild: node => openCreate(node.id),
    onDelete: remove,
  }

  return (
    <Card
      title="選單管理"
      extra={
        <Button type="primary" icon={<PlusOutlined />} disabled={isDirty} onClick={() => openCreate(null)}>
          新增選單
        </Button>
      }
    >
      <Typography.Paragraph type="secondary" style={{ fontSize: 'var(--fs-sm)', marginBottom: 12 }}>
        拖曳 <HolderOutlined /> 調整同層順序（跨層請用編輯更改上級選單）；每列可直接「＋子選單」。
      </Typography.Paragraph>

      {isDirty && (
        <Alert
          type="warning" showIcon style={{ marginBottom: 12 }}
          message="排序已調整，尚未儲存"
          action={
            <Space>
              <Button size="small" type="primary" loading={saving} onClick={confirmReorder}>儲存排序</Button>
              <Button size="small" onClick={() => setWorkingTree(null)}>取消</Button>
            </Space>
          }
        />
      )}

      {loading ? (
        <div style={{ textAlign: 'center', padding: 40 }}><Spin /></div>
      ) : (
        <DndContext sensors={sensors} collisionDetection={closestCenter}
          modifiers={[restrictToVerticalAxis]} onDragEnd={handleDragEnd}>
          <ul className="menu-list">
            <SortableContext items={displayTree.map(n => n.id)} strategy={verticalListSortingStrategy}>
              {displayTree.map(node => <MenuRow key={node.id} node={node} depth={0} ctx={rowCtx} />)}
            </SortableContext>
          </ul>
        </DndContext>
      )}

      <Modal
        title={editing?.id == null ? '新增選單' : '編輯選單'}
        open={editing !== null} onOk={save} onCancel={() => setEditing(null)} destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 12 }}>
          <Form.Item name="name" label="選單名稱" rules={[{ required: true, message: '請輸入選單名稱' }]}>
            <Input />
          </Form.Item>
          <Form.Item name="parentId" label="上級選單（留空＝第一層）">
            <TreeSelect allowClear treeData={parentOptions} treeDefaultExpandAll placeholder="選擇上級選單" />
          </Form.Item>
          <Form.Item name="path" label="路由路徑（留空＝目錄）"
            extra={editing?.hasChildren ? '此選單底下有子選單（屬於目錄），不能設定路徑' : undefined}>
            <Input placeholder="/users" disabled={editing?.hasChildren} />
          </Form.Item>
          <Form.Item name="permissionCode" label="權限代碼"
            extra="與角色管理連動：在角色勾選此選單＝授予此權限；留空則所有登入者皆可見（目錄通常留空）。新功能頁的代碼由開發方於上線時設定，一般不需變更。">
            <Select allowClear placeholder="（不設定）"
              options={permissions.map(p => ({ value: p.code, label: `${p.name}（${p.code}）` }))} />
          </Form.Item>
          <Form.Item name="icon" label="圖示">
            <Select
              allowClear showSearch placeholder="選擇圖示"
              optionLabelProp="label"
              options={ICON_NAMES.map(name => ({
                value: name,
                label: <Space>{iconOf(name)}<span>{name}</span></Space>,
              }))}
              filterOption={(input, option) =>
                String(option?.value ?? '').toLowerCase().includes(input.toLowerCase())}
            />
          </Form.Item>
          <Form.Item name="isActive" label="狀態" valuePropName="checked">
            <Switch checkedChildren="啟用" unCheckedChildren="停用" />
          </Form.Item>
        </Form>
      </Modal>
    </Card>
  )
}
