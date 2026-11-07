import { useEffect, useState } from 'react'
import { Card, List, Tag, Button, Empty, Typography, Space, Skeleton } from 'antd'
import {
  AuditOutlined, UserOutlined, RightOutlined, CheckSquareOutlined,
} from '@ant-design/icons'
import { useNavigate } from 'react-router-dom'
import { api } from '../api'

type Severity = 'urgent' | 'warn' | 'info'

interface TodoItem {
  key: string
  group: string
  title: string
  detail: string
  count: number
  route: string
  tab: string | null
  severity: Severity
}

/**
 * 群組的視覺與圖示。⚠ 群組字串由**後端**決定 ——
 * 前端只負責把不認得的群組也畫得出來，不去猜有哪幾種。
 */
const GROUP_ICON: Record<string, React.ReactNode> = {
  簽核: <AuditOutlined />,
  我的: <UserOutlined />,
}

const SEVERITY: Record<Severity, { color: string; bg?: string; label?: string }> = {
  urgent: { color: 'var(--c-danger)', bg: 'var(--c-urgent-bg)', label: '待處理' },
  warn: { color: 'var(--c-primary)' },
  info: { color: 'var(--c-text-muted)' },
}

/**
 * 登入後的待辦清單。
 *
 * ⚠ 資料由**單一聚合端點**供應，前端不逐支呼叫各模組 ——
 * 沒有權限的人會拿到一串 403，而讓前端自己判斷「我有沒有資格看這項」
 * 就是把可見範圍的判斷搬到前端，那是本專案已經修過一輪的整類缺陷。
 *
 * ⚠ 落點（route / tab）也由後端給。同一則待辦日後改掛別的頁面時，
 * 只有後端那一行要改，不必回來翻這裡的對照表。
 */
export default function TodoPanel() {
  const [items, setItems] = useState<TodoItem[] | null>(null)
  const [failed, setFailed] = useState(false)
  const nav = useNavigate()

  useEffect(() => {
    api.get<{ items: TodoItem[] }>('/api/admin/dashboard/todo')
      .then(r => setItems(r.items))
      .catch(() => { setFailed(true); setItems([]) })
  }, [])

  const go = (it: TodoItem) => nav(it.tab ? `${it.route}?tab=${it.tab}` : it.route)

  /* ⚠ 標題的數字只算「要你動手的」。
     把知悉性質的項目也加進去，
     標題的數字會誇大要做的事。
     數字誇大的提醒，第二次就沒有人看了。 */
  const total = items?.filter(i => i.severity !== 'info')
    .reduce((n, i) => n + i.count, 0) ?? 0
  const urgent = items?.some(i => i.severity === 'urgent') ?? false

  return (
    <Card
      title={
        <Space>
          <CheckSquareOutlined />
          待辦事項
          {total > 0 && (
            <Tag color={urgent ? 'error' : 'warning'} style={{ marginInlineStart: 4 }}>
              {total}
            </Tag>
          )}
        </Space>
      }
    >
      {items === null && <Skeleton active paragraph={{ rows: 3 }} />}

      {items !== null && items.length === 0 && (
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={failed
            ? '待辦載入失敗，重新整理再試一次'
            : '目前沒有待辦事項'} />
      )}

      {items !== null && items.length > 0 && (
        <List
          size="small"
          dataSource={items}
          renderItem={it => {
            const s = SEVERITY[it.severity] ?? SEVERITY.info
            return (
              <List.Item
                style={s.bg ? { background: s.bg, borderRadius: 6, paddingInline: 8 } : undefined}
                actions={[
                  // info 是知悉性質，說「前往處理」等於叫人去做一件他不必做的事
                  <Button size="small" type="link" onClick={() => go(it)}>
                    {it.severity === 'info' ? '查看' : '前往處理'} <RightOutlined />
                  </Button>,
                ]}
              >
                <List.Item.Meta
                  avatar={<span style={{ color: s.color, fontSize: 'var(--fs-lg)' }}>
                    {GROUP_ICON[it.group] ?? <AuditOutlined />}
                  </span>}
                  title={
                    <Space size={6} style={{ fontSize: 'var(--fs-md)' }}>
                      <Tag color="default" style={{ marginInlineEnd: 0 }}>{it.group}</Tag>
                      <span>{it.title}</span>
                      {/* 筆數用強調色而不是另開一欄：使用者掃的是數字 */}
                      <b style={{ color: s.color }}>{it.count}</b>
                    </Space>
                  }
                  description={
                    <Typography.Text type="secondary" style={{ fontSize: 'var(--fs-sm)' }}>
                      {it.detail}
                    </Typography.Text>
                  }
                />
              </List.Item>
            )
          }}
        />
      )}
    </Card>
  )
}
