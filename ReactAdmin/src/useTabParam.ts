import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'

/**
 * 分頁狀態，可由網址的 <c>?tab=</c> 指定初始值。
 *
 * ⚠ 存在的理由：首頁待辦點下去要**落在對應分頁**，而不是落在該頁的預設分頁。
 * 後端已經在每一則待辦上給了 route 與 tab（見 AdminDashboardController.TodoItem），
 * 前端若不讀這個參數，網址上的 `?tab=confirm` 就只是一段沒有作用的字 ——
 * 使用者點「班表待你確認」會落在「班表」分頁，還得自己再點一次。
 *
 * ⚠ 只認 <paramref name="allowed"/> 之內的值。網址是使用者可控的，
 * 塞一個不存在的分頁代碼會讓 antd 的 Tabs 一片空白。
 *
 * 切換分頁時**不寫回網址** —— 這個參數是「從外面帶進來的落點」，
 * 不是頁面狀態；寫回去會讓上一頁／重新整理的行為變得難以預期。
 */
export function useTabParam(fallback: string, allowed: readonly string[]) {
  const [params] = useSearchParams()
  const wanted = params.get('tab')
  const incoming = wanted && allowed.includes(wanted) ? wanted : null
  const [tab, setTab] = useState(incoming ?? fallback)

  /*
   * ⚠⚠ 網址上的 ?tab= 變了就要跟著跳。
   *
   * 原本只用 useState 取初始值，而 useState 的初始值**只在第一次掛載時讀** ——
   * 於是「人已經在 /attendance，再點側邊欄的『補卡申請』(/attendance?tab=corrections)」
   * 這個最常見的情況**完全沒反應**：路由變了、元件沒重新掛載、分頁還停在原地。
   * （2026-08-23 加「補卡申請」選單時發現。）
   *
   * ⭐ 只在「外面帶進來的值」變動時跳，**不寫回網址** —— 維持原本的設計意圖：
   *   這個參數是落點，不是頁面狀態。
   * ⚠ 已知邊界：已經在 ?tab=corrections、手動切到別的分頁、再點一次同一個選單
   *   → 網址沒變，不會跳回去。這種情況少，且再點一次別的選單就恢復。
   */
  useEffect(() => { if (incoming) setTab(incoming) }, [incoming])

  return [tab, setTab] as const
}
