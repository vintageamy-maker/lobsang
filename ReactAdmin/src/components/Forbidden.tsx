import { Result } from 'antd'
import { LockOutlined } from '@ant-design/icons'

/**
 * 「你沒有這一頁的權限」。
 *
 * ⚠ 存在的理由：後端回 403 時，頁面若只是安靜地顯示一張空表格，
 * 使用者看到的是「這裡沒有資料」而不是「你不能看這裡」——
 * 兩者會導出完全不同的下一步（前者去問資料為什麼不見，後者去要權限）。
 * 選單本來就不會顯示無權限的項目，所以要刻意打網址才會遇到；
 * 但既然遇得到，就不該讓人誤會。
 */
export default function Forbidden({ what }: { what?: string }) {
  return (
    <Result
      icon={<LockOutlined style={{ color: 'var(--c-text-muted)' }} />}
      title="沒有這一頁的權限"
      subTitle={
        <>
          你的帳號未被授予{what ? `「${what}」` : '這個功能'}的權限，因此看不到內容。
          <br />
          需要使用的話請洽系統管理員調整角色。
        </>
      }
    />
  )
}
