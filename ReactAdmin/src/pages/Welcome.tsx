import { Card, Tag, Space, Typography, Descriptions, Collapse } from 'antd'
import { auth } from '../api'
import TodoPanel from '../components/TodoPanel'

/**
 * 登入後的首頁。
 *
 * ⚠ 這一頁回答的是「**我現在要做什麼**」，不是「這個系統有哪些功能」。
 * 改版前它列的是一整片權限碼與一段靜態的功能介紹 —— 對使用者沒有意義，
 * 而且功能介紹會隨著開發進度過期，沒有人會記得回來改。
 *
 * 權限碼留著但收起來：除錯時（「他為什麼看不到那個選單」）還是最快的答案。
 */
export default function Welcome() {
  const user = auth.user()

  return (
    <Space direction="vertical" size={16} style={{ display: 'flex' }}>
      <div>
        <Typography.Title level={4} style={{ marginBottom: 0 }}>
          {greeting()}，{user?.name}
        </Typography.Title>
        <Typography.Text type="secondary">
          {user?.orgUnitName ?? '—'}　{user?.title ?? ''}
        </Typography.Text>
      </div>

      <TodoPanel />

      <Card size="small" title="我的帳號">
        <Descriptions size="small" column={{ xs: 1, sm: 2 }}>
          <Descriptions.Item label="帳號">{user?.account}</Descriptions.Item>
          <Descriptions.Item label="職稱">{user?.title ?? '—'}</Descriptions.Item>
          <Descriptions.Item label="歸屬單位">{user?.orgUnitName ?? '—'}</Descriptions.Item>
          <Descriptions.Item label="角色">
            <Space wrap>{user?.roles.map(r => <Tag key={r} color="orange">{r}</Tag>)}</Space>
          </Descriptions.Item>
        </Descriptions>

        <Collapse
          ghost size="small" style={{ marginTop: 4 }}
          items={[{
            key: 'perms',
            label: <Typography.Text type="secondary" style={{ fontSize: 'var(--fs-sm)' }}>
              權限碼（{user?.permissions.length ?? 0}）—— 排查「為什麼看不到某個選單」時用
            </Typography.Text>,
            children: (
              <Space wrap>
                {user?.permissions.map(p => <Tag key={p}>{p}</Tag>)}
              </Space>
            ),
          }]}
        />
      </Card>
    </Space>
  )
}

/** 依時段問候。小事，但首頁每天都會被看到 */
function greeting() {
  const h = new Date().getHours()
  if (h < 6) return '夜深了'
  if (h < 12) return '早安'
  if (h < 18) return '午安'
  return '晚安'
}
