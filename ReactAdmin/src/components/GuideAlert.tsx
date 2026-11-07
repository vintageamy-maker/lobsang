import { useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { Alert } from 'antd'
import { auth } from '../api'

/**
 * 常駐說明用的 Alert：**說明預設收起、現況永遠顯示**。
 *
 * ## 為什麼要分兩塊
 *
 * 改版前全站 42 個 `<Alert>`、`closable` 與 `<Collapse>` 都是 0 —— 全部永遠展開。
 * 但**不能整批收起來**，因為同一個 Alert 裡混了兩種東西。以差勤判定規則為例：
 *
 * - 「這個欄位怎麼算」這類規則說明 → **教學**，第一次要看，第 200 次不用
 * - 「⏱ 目前以 2026-08-01 為判定基準（非現在時間）」 → **現況**，收起來會誤判
 * - 「某參數尚未確認，暫用預設值」 → **未決事項**，收起來等於沒問過
 *
 * ⚠ **判準：這句話會不會隨資料改變。** 會變的放 `live`（永遠顯示），
 *   不會變的放 `children`（可收）。收錯邊，就是用「永遠展開」換到「沒人在看」。
 *
 * ## 什麼情況不要用這個元件
 *
 * - **狀態與資料**（「已選 3 筆」「此記錄已撤銷」「本期有 5 筆違規」）—— 那本來就該一直在
 * - **Modal／Drawer 裡的說明** —— 使用者是**主動點開來看**的，說明就是他要的東西
 *   （例：「怎麼看這張表」）
 * - **條件觸發的提醒**（例：§49 III 妊娠夜間工作）—— 收起來就等於把提醒關掉
 */
type Props = {
  /** 記憶用的識別碼，全站唯一且**不要再改**（改了等於使用者的收合設定被重置） */
  id: string
  /** 標題：收合時仍然看得到，所以要能單獨說明這塊在講什麼 */
  title: ReactNode
  /** 教學內容（可收）。沒有就只是一個普通 Alert */
  children?: ReactNode
  /** 現況／警告（永遠顯示，不受收合影響） */
  live?: ReactNode
  type?: 'info' | 'warning' | 'error' | 'success'
  style?: CSSProperties
}

/*
 * ⚠ 收合狀態要**綁使用者**，不能只綁瀏覽器 —— 可能是共用電腦，
 *   否則一個人收起來，下一個登入的人也跟著看不到。
 */
const keyOf = (id: string) => `lobsang_guide_${auth.user()?.id ?? 'anon'}_${id}`

export default function GuideAlert({ id, title, children, live, type = 'info', style }: Props) {
  const [open, setOpen] = useState(() => localStorage.getItem(keyOf(id)) === '1')

  const toggle = () => {
    const next = !open
    setOpen(next)
    // 無痕模式或儲存空間滿了會丟例外；記不起來不影響這次的顯示，不要讓它炸掉整頁
    try { localStorage.setItem(keyOf(id), next ? '1' : '0') } catch { /* 記不住就算了 */ }
  }

  const body = open ? children : null

  return (
    <Alert
      type={type}
      showIcon
      style={style}
      message={
        <span style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <span>{title}</span>
          {children != null && (
            <a
              onClick={toggle}
              aria-expanded={open}
              style={{ fontSize: 'var(--fs-sm)', fontWeight: 400 }}
            >
              {open ? '收合說明' : '看說明'}
            </a>
          )}
        </span>
      }
      /* 兩塊都沒有時要回 undefined，Alert 才會縮成單行，不會留一條空白 */
      description={body || live ? <>{body}{live}</> : undefined}
    />
  )
}
