import {
  SettingOutlined, TeamOutlined, UserOutlined, IdcardOutlined, SafetyOutlined,
  MenuOutlined, HomeOutlined, FileTextOutlined, FormOutlined, CalendarOutlined,
  BarChartOutlined, PieChartOutlined, DatabaseOutlined, FolderOutlined,
  BellOutlined, ClockCircleOutlined, HeartOutlined, MedicineBoxOutlined,
  BookOutlined, SolutionOutlined, AuditOutlined, ApartmentOutlined, AimOutlined,
  ReconciliationOutlined,
  CheckSquareOutlined, LineChartOutlined, NodeIndexOutlined,
  GiftOutlined, InboxOutlined, CarOutlined, ToolOutlined, MessageOutlined, ShopOutlined,
  CameraOutlined, DollarOutlined, TableOutlined,
  CoffeeOutlined, ExperimentOutlined,
  // 2026-08-22 補：這四個 key 早就被選單種子用了，但一直沒註冊 → 那五項一直沒有圖示
  FileOutlined, CrownOutlined, LockOutlined, ScheduleOutlined,
  BankOutlined, WalletOutlined, SwapOutlined,
  AppstoreOutlined, BranchesOutlined,
} from '@ant-design/icons'
import type { ReactNode } from 'react'

/**
 * 選單圖示註冊表：DB 只存 key（如 "setting"），前端據此渲染。
 * 側邊欄（AdminLayout）、選單管理（列表與表單快選）共用同一份。
 */
export const ICON_MAP: Record<string, ReactNode> = {
  'setting': <SettingOutlined />,
  'team': <TeamOutlined />,
  'user': <UserOutlined />,
  'idcard': <IdcardOutlined />,
  'safety': <SafetyOutlined />,
  'menu': <MenuOutlined />,
  'home': <HomeOutlined />,
  'file-text': <FileTextOutlined />,
  'form': <FormOutlined />,
  'calendar': <CalendarOutlined />,
  'bar-chart': <BarChartOutlined />,
  'pie-chart': <PieChartOutlined />,
  'database': <DatabaseOutlined />,
  'bank': <BankOutlined />,
  'wallet': <WalletOutlined />,
  'swap': <SwapOutlined />,
  'folder': <FolderOutlined />,
  'bell': <BellOutlined />,
  'clock': <ClockCircleOutlined />,
  'heart': <HeartOutlined />,
  'medicine': <MedicineBoxOutlined />,
  'book': <BookOutlined />,
  'solution': <SolutionOutlined />,
  'reconciliation': <ReconciliationOutlined />,
  'audit': <AuditOutlined />,
  'aim': <AimOutlined />,
  'check-square': <CheckSquareOutlined />,
  'line-chart': <LineChartOutlined />,
  'node-index': <NodeIndexOutlined />,
  'apartment': <ApartmentOutlined />,
  'gift': <GiftOutlined />,
  'inbox': <InboxOutlined />,
  'car': <CarOutlined />,
  'tool': <ToolOutlined />,
  'shop': <ShopOutlined />,

  /*
   * ── 2026-08-22 補上四個「種子有用、這裡卻沒註冊」的 key ──────────────
   *
   * ⚠⚠ 症狀是**只有那幾項沒有圖示**，其餘正常 —— 從畫面上看不出原因，
   *    很容易被當成「那幾頁忘了設圖示」，但資料庫裡其實都有值。
   *
   * ⭐ iconOf() 找不到時只在 console 印一次警告就回 undefined，
   *    不會壞掉、也不會有紅字 —— 靜默失效，所以拖到現在才被發現。
   *
   * ⇒ 新增選單時：**種子填的 key 一定要同時加進這張表**。
   *   （'shop' 那次是同一個坑，當天就補了；這四個是更早留下來的。）
   */
  'file': <FileOutlined />,
  'crown': <CrownOutlined />,
  'lock': <LockOutlined />,          // 我的認證密碼
  'schedule': <ScheduleOutlined />,
  'message': <MessageOutlined />,
  'camera': <CameraOutlined />,
  'coffee': <CoffeeOutlined />,
  'experiment': <ExperimentOutlined />,
  'dollar': <DollarOutlined />,
  // ⚠ 註冊表缺 key 是靜默失效：新增選單種子時，圖示 key 必須同時在此登記。
  'table': <TableOutlined />,
  'appstore': <AppstoreOutlined />,
  'branches': <BranchesOutlined />,
}

export const ICON_NAMES = Object.keys(ICON_MAP)

/**
 * 依 key 取圖示。
 *
 * ⚠ 查不到時回傳 undefined ——「沒有圖示」與「圖示打錯字」在畫面上長得一模一樣，
 * 兩者都只是少一個小圖，不會報錯。這種靜默失效是選單種子改動後最容易漏掉的一項，
 * 所以把找不到的 key 印出來。
 */
const warned = new Set<string>()

export function iconOf(name: string | null | undefined): ReactNode | undefined {
  if (!name) return undefined
  const icon = ICON_MAP[name]
  if (!icon && !warned.has(name)) {
    // 每個 key 只提醒一次 —— 側邊欄每次重繪都會呼叫，不去重會洗版
    warned.add(name)
    console.warn(`[icons] 選單圖示「${name}」不在 ICON_MAP 裡，該項不會顯示圖示`)
  }
  return icon
}
