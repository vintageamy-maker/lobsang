import { useCallback, useEffect, useMemo, useState } from 'react'
import { Outlet, useLocation, useNavigate, Navigate, Link } from 'react-router-dom'
import { Layout, Menu, Dropdown, Avatar, Spin, Button, Drawer, Grid, Breadcrumb, type MenuProps } from 'antd'
import {
  LogoutOutlined, HomeOutlined, MenuOutlined, ReloadOutlined, WarningOutlined, KeyOutlined,
} from '@ant-design/icons'
import ChangePasswordModal from '../components/ChangePasswordModal'
import { api, auth, type MenuNode } from '../api'
import { iconOf } from '../icons'

/*
 * 選單載入有三種結果，必須分得開：
 *   loading —— 還在等
 *   ready   —— 拿到了（有可能真的是空陣列＝這個人沒有任何模組權限）
 *   error   —— 打不到 API
 * ⚠ 原本 error 被 `.catch(() => setMenus([]))` 併進 ready，畫面上「只剩首頁」，
 *   跟「權限被拿掉」長得一模一樣，而且不出任何訊息 —— 是靜默失效。
 */
type MenuState =
  | { kind: 'loading' }
  | { kind: 'ready'; nodes: MenuNode[] }
  | { kind: 'error'; message: string }

/*
 * 不在選單上、而是從某一頁點進去的子頁 → 它屬於哪一個選單。
 *
 * 沒有這張表的話，進到子頁麵包屑只剩「首頁」、側邊欄也沒有任何一項亮著。
 * 例：'/xxx/detail': { parent: '/xxx', name: '明細' }
 * ⚠ 選單上真的有這個路徑時以選單為準，這張表不介入。
 * ⚠ 上層那一項不在這個人的選單裡（沒權限）時，維持「查不到就只顯示首頁」，不猜。
 */
const SUB_PAGES: Record<string, { parent: string; name: string }> = {}

export default function AdminLayout() {
  const navigate = useNavigate()

  /**
   * 登出。
   *
   * ⚠ 除了清掉登入權杖，還要**主動作廢認證密碼的解鎖工作階段** ——
   * 否則伺服器上會留著一個還活著的解鎖紀錄，直到閒置逾時才失效。
   * （後端另有「登入即作廢」把關，這裡是為了讓它**立刻**失效而不是等到下次登入。）
   */
  const logout = async () => {
    try { await api.post('/api/auth/step-up/logout') } catch { /* 沒解鎖過或已失效都無妨 */ }
    auth.clear()
    navigate('/login')
  }
  const location = useLocation()
  const screens = Grid.useBreakpoint()
  const isMobile = !screens.lg
  const user = auth.user()
  const [menuState, setMenuState] = useState<MenuState>({ kind: 'loading' })
  // 行動版：側邊欄改為可開關的抽屜。⚠ 純行動版語意，不與桌機的 collapsed 混用
  const [navOpen, setNavOpen] = useState(false)

  /*
   * 修改登入密碼（2026-08-22）。
   *
   * ⚠⚠ `mustChangePassword` 為真時要**自動開啟且關不掉** ——
   *    後端建立帳號時會把它設成 true，但前端在此之前既不讀它、
   *    也沒有換密碼的畫面，那個機制等於完全沒有作用，
   *    而被設到的人也沒有出路。
   */
  const [pwdOpen, setPwdOpen] = useState(false)
  const mustChange = !!user?.mustChangePassword
  useEffect(() => { if (mustChange) setPwdOpen(true) }, [mustChange])

  /*
   * 桌機版收合成圖示軌。
   * ⚠ key 綁**使用者**不是只綁瀏覽器 —— 可能是共用電腦，
   *   否則一個人收起來，下一個登入的人也跟著是收的（同 GuideAlert 的理由）。
   */
  const collapseKey = `lobsang_nav_${auth.user()?.id ?? 'anon'}_collapsed`
  const [collapsed, setCollapsed] = useState(() => {
    // 讀取也包 try —— 停用 cookie／無痕模式下，光是碰 localStorage 就可能丟例外
    try { return localStorage.getItem(collapseKey) === '1' } catch { return false }
  })
  const applyCollapsed = (next: boolean) => {
    setCollapsed(next)
    try { localStorage.setItem(collapseKey, next ? '1' : '0') } catch { /* 記不住就算了 */ }
  }
  const toggleCollapsed = () => applyCollapsed(!collapsed)

  /*
   * 滑鼠移到軌道上就暫時展開（暫時，不動使用者存下來的偏好）。
   *
   * ⚠ 展開時 Sider 以絕對定位**浮在內容上方**，不是把內容推開 ——
   *   軌道那 64px 的位置始終保留。否則滑鼠只是經過側邊就會讓整頁往右跳 156px，
   *   正在讀的內容從游標底下跑掉。這也是 [[52]] 明列「不建議照抄」的推擠式選單。
   */
  const [hovering, setHovering] = useState(false)

  /** 桌機、已收合、且滑鼠不在上面＝圖示軌模式。行動版走抽屜，與收合無關 */
  const railMode = !isMobile && collapsed && !hovering

  const menus = menuState.kind === 'ready' ? menuState.nodes : null

  const loadMenus = useCallback(() => {
    if (!auth.token()) return
    setMenuState({ kind: 'loading' })
    api.get<MenuNode[]>('/api/auth/menus')
      .then(nodes => setMenuState({ kind: 'ready', nodes }))
      // 401 由 api.request 統一處理（清權杖並導回登入），不會走到這裡；
      // 這條是連線失敗或 5xx —— 所以「不是權限問題」這句話是成立的。
      .catch((e: unknown) => setMenuState({
        kind: 'error',
        message: e instanceof Error ? e.message : '無法連線',
      }))
  }, [])

  useEffect(() => { loadMenus() }, [loadMenus])

  // 切回桌機寬度時關閉抽屜，避免殘留開啟狀態
  useEffect(() => { if (!isMobile) setNavOpen(false) }, [isMobile])

  /*
   * 圖示軌下點群組圖示 → 永久展開側邊欄並打開該群組。
   *
   * ⚠ 收合時第一層幾乎都是**目錄**（只有「首頁」是真正的頁面），目錄沒有路徑、
   *   點了本來就不會導覽 —— 業主回報的「只有首頁點了有反應」正是這個。
   *
   * 有了滑過即展開之後，滑鼠使用者幾乎碰不到這條路徑（一進到軌道上就展開了）；
   * 但**觸控裝置沒有 hover**，這條是它們唯一的出路，所以保留。
   */
  const expandInto = (key: string) => {
    applyCollapsed(false)
    setHovering(false)
    setInlineOpenKeys(prev => (prev.includes(key) ? prev : [...prev, key]))
  }

  const items: MenuProps['items'] = useMemo(() => {
    const toItem = (n: MenuNode, depth: number): NonNullable<MenuProps['items']>[number] => {
      const key = n.path ?? `dir-${n.id}`
      const hasChildren = n.children.length > 0
      return {
        key,
        icon: iconOf(n.icon),
        label: n.name,
        children: hasChildren ? n.children.map(c => toItem(c, depth + 1)) : undefined,
        // ⚠ 只接管圖示軌的**第一層** —— 彈出面板裡的下一層維持 antd 原本的行為，
        //    否則在面板裡想往下鑽（班表編排 → 四週變形工時）會被整個側邊欄展開打斷
        ...(hasChildren && railMode && depth === 0 ? { onTitleClick: () => expandInto(key) } : {}),
      }
    }
    return [
      { key: '/', icon: <HomeOutlined />, label: '首頁' },
      ...(menus ?? []).map(m => toItem(m, 0)),
    ]
  }, [menus, railMode])

  /*
   * 麵包屑：路徑 → 各層名稱。選單樹本來就在記憶體裡（AdminLayout 已為了側欄撈過），
   * 所以不必新增 API，也不必在各頁重複宣告標題。
   * ⚠ 查不到就回空陣列，只顯示「首頁」—— 寧可少一層，也不要猜一個錯的名字。
   *   （轉址路由與尚未掛進選單的頁面會走到這條）
   */
  const trailOf = useMemo(() => {
    const map = new Map<string, string[]>()
    const walk = (n: MenuNode, ancestors: string[]) => {
      const trail = [...ancestors, n.name]
      if (n.path) map.set(n.path, trail)
      n.children.forEach(c => walk(c, trail))
    }
    ;(menus ?? []).forEach(m => walk(m, []))
    return map
  }, [menus])

  /** 目前這一頁若是「不在選單上的子頁」，它的上層選單（見 SUB_PAGES） */
  const sub: { parent: string; name: string } | undefined = SUB_PAGES[location.pathname]
  const parentTrail = sub ? trailOf.get(sub.parent) : undefined
  const trail = trailOf.get(location.pathname)
    ?? (sub && parentTrail ? [...parentTrail, sub.name] : [])

  /*
   * 路徑 → 其所有上層目錄的 key。選單自 2026-08-14 起有三層
   * （目錄 → 子目錄 → 頁面），第二層目錄若不展開，
   * 使用者每次都要多點一次才看得到自己正在瀏覽的頁面。
   */
  const ancestorsOf = useMemo(() => {
    const map = new Map<string, string[]>()
    const walk = (n: MenuNode, ancestors: string[]) => {
      const key = n.path ?? `dir-${n.id}`
      if (n.path) map.set(n.path, ancestors)
      n.children.forEach(c => walk(c, [...ancestors, key]))
    }
    ;(menus ?? []).forEach(m => walk(m, []))
    return map
  }, [menus])

  /** 所有選單路徑（含帶查詢字串的那種），供選取高亮比對用 */
  const menuPaths = useMemo(() => {
    const set = new Set<string>()
    const walk = (n: MenuNode) => { if (n.path) set.add(n.path); n.children.forEach(walk) }
    ;(menus ?? []).forEach(walk)
    return set
  }, [menus])

  /*
   * 展開狀態改為受控。
   *
   * ⚠ 原本用 defaultOpenKeys —— 那個 prop 只在 <Menu> 掛載當下讀一次，
   *   所以只能展開第一層，且之後導覽到收合目錄底下的頁面時打不開
   *   （首頁待辦的連結會直接跳過去，就會發生）。
   *
   * 規則（2026-08-22 業主指示改）：**預設全部收起來**，
   * 只把「目前頁面的上層目錄」打開 —— 使用者正在看的那一條才需要攤開。
   *
   * ⚠ 原本是「第一層目錄一律展開」。模組愈加愈多之後，一進來整片攤開，
   *   選單長到要捲動才找得到東西，等於沒有分類。
   *   ⚠ 這也表示**第一次進首頁時側邊欄會是全收的** —— 那是刻意的，不是壞了。
   *
   * ⚠⚠ 展開模式與圖示軌模式的 openKeys **必須是兩份**。
   *   兩者在 rc-menu 眼中是同一個 prop，語意卻完全不同：
   *     展開模式 = 哪些目錄是攤開的（要跨收合／展開保存）
   *     圖示軌   = 哪個群組的**彈出面板**正顯示著（滑過即開、移開即關，不必保存）
   *   2026-08-14 第一版為了保住展開狀態，在圖示軌時鎖成 [] 又不接受回呼 ——
   *   結果彈出面板永遠開不起來，看起來就是「圖示點了沒反應」。
   */
  const [inlineOpenKeys, setInlineOpenKeys] = useState<string[]>([])
  const [railOpenKeys, setRailOpenKeys] = useState<string[]>([])
  useEffect(() => {
    // ⚠ 子頁本身不在選單上 ⇒ 改展開它上層那一項所在的目錄
    const current = ancestorsOf.get(location.pathname)
      ?? (sub ? ancestorsOf.get(sub.parent) : undefined) ?? []
    setInlineOpenKeys(prev => {
      /*
       * 只補「目前頁面的上層目錄」，其餘一律不動。
       * ⚠ 這裡刻意**只加不減** —— 使用者自己展開的其他目錄要留著，
       *   否則每導覽一次就會把他攤開的東西關掉，像是被系統跟他搶。
       */
      const missing = current.filter(k => !prev.includes(k))
      return missing.length ? [...prev, ...missing] : prev
    })
  }, [menus, ancestorsOf, location.pathname])

  if (!auth.token()) return <Navigate to="/login" replace />

  const handleMenuClick: MenuProps['onClick'] = ({ key }) => {
    if (!String(key).startsWith('/')) return
    navigate(String(key))
    setNavOpen(false)   // 行動版點選後自動收起抽屜
  }
  const navContent =
    menuState.kind === 'loading' ? (
      <div style={{ textAlign: 'center', paddingTop: 40 }}><Spin /></div>
    ) : menuState.kind === 'error' ? (
      <div style={{ padding: '32px 20px', textAlign: 'center', color: 'rgba(255,255,255,.65)' }}>
        <WarningOutlined style={{ fontSize: 'var(--fs-xl)', display: 'block', marginBottom: 10 }} />
        <div style={{ fontSize: 'var(--fs-md)', lineHeight: 1.7 }}>
          選單載入失敗<br />
          <span style={{ fontSize: 'var(--fs-sm)' }}>這不是權限問題，請重試</span>
          <div style={{ fontSize: 'var(--fs-xs)', color: 'rgba(255,255,255,.45)', marginTop: 6 }}>
            {menuState.message}
          </div>
        </div>
        <Button size="small" icon={<ReloadOutlined />} onClick={loadMenus} style={{ marginTop: 12 }}>
          重新載入
        </Button>
      </div>
    ) : (
      /*
       * openKeys 依模式切換到兩份不同的 state（理由見上方宣告處）。
       *
       * 這樣同時解掉兩件事：
       *   1. rc-menu 收合時會主動呼叫 onOpenChange([])，展開時卻不回呼
       *      （@rc-component/menu 的 Menu.js「Cache & Reset open keys」那段）。
       *      那個 [] 現在只會落到 railOpenKeys，展開模式的狀態不受影響。
       *   2. 圖示軌的彈出面板需要真的能開關 —— 交給 railOpenKeys 自己管。
       *
       * 另：不必傳 inlineCollapsed —— antd 的 Menu 會從 SiderContext 讀 siderCollapsed。
       */
      /*
       * ⚠ selectedKeys：有些選單的路徑帶查詢字串（補卡申請＝/attendance?tab=corrections）——
       *   只比對 pathname 的話它永遠不會被選中，反而會把「差勤紀錄」點亮。
       * ⭐ 先試「路徑＋查詢字串」，有對到就用它；沒有才退回只比路徑。
       */
      <Menu
        theme="dark" mode="inline" items={items}
        selectedKeys={[
          menuPaths.has(location.pathname + location.search)
            ? location.pathname + location.search
            // 子頁不在選單上 ⇒ 點亮它的上層
            : !menuPaths.has(location.pathname) && sub && menuPaths.has(sub.parent)
              ? sub.parent
              : location.pathname,
        ]}
        openKeys={railMode ? railOpenKeys : inlineOpenKeys}
        onOpenChange={keys =>
          railMode ? setRailOpenKeys(keys as string[]) : setInlineOpenKeys(keys as string[])}
        onClick={handleMenuClick}
      />
    )

  /*
   * ⚠ 判斷用 railMode（含 !isMobile）而不是只看 collapsed ——
   *   brand 是 Sider 與 Drawer 共用的，只看 collapsed 的話，
   *   桌機收合後切到手機，抽屜裡的站名會變成縮寫。
   * nowrap + hidden：收合的寬度動畫期間讓文字被裁掉，而不是換行抖一下。
   */
  const brand = (
    <div style={{
      color: 'var(--c-primary-border)', fontWeight: 800, fontSize: 'var(--fs-lg)', letterSpacing: 1,
      padding: railMode ? '18px 0' : '18px 20px',
      textAlign: railMode ? 'center' : undefined,
      whiteSpace: 'nowrap', overflow: 'hidden',
      borderBottom: '1px solid rgba(255,255,255,.08)',
    }}>
      {railMode ? 'L' : 'Lobsang 後台'}
    </div>
  )

  const userMenu: MenuProps['items'] = [
    /* ⭐ 2026-08-22 新增。在此之前**沒有任何地方**能改自己的登入密碼 ——
     *   後端 API 一直都在，只是前端沒有入口，同仁只能請管理員代為重設。 */
    { key: 'password', icon: <KeyOutlined />, label: '修改密碼' },
    { type: 'divider' },
    { key: 'logout', icon: <LogoutOutlined />, label: '登出' },
  ]

  return (
    <Layout style={{ minHeight: '100vh' }}>
      {!isMobile && (
        /*
         * 外層只負責「在版面裡佔住多少寬度」，收合時固定 64px 不隨滑過改變；
         * 真正的 Sider 絕對定位在它上面，滑過展開時浮起來、內容區不位移。
         *
         * ⚠ sticky ＋ 100vh（2026-08-15）：側邊欄永遠剛好一個畫面高並釘在畫面上。
         *   原本是 relative ＋ 預設拉伸，高度會等於「整份文件」——
         *   短頁面時側邊欄只有一個畫面高、選單卻更長，就被裁掉且**構不到**（業主回報的症狀）；
         *   長頁面時側邊欄雖然夠高，卻會隨頁面捲出畫面外。
         *
         *   alignSelf: 'flex-start' 是必要的 —— flex 子項預設 stretch，
         *   不解除的話 height:100vh 會被拉伸覆蓋，sticky 也就沒有作用。
         *
         *   sticky 與 relative 一樣會成為絕對定位子元素的**包含區塊**，
         *   所以裡面那個 position:absolute 的 Sider（滑過浮起用）行為完全不變。
         */
        <div
          style={{
            position: 'sticky', top: 0,
            alignSelf: 'flex-start',
            height: '100vh',
            /*
             * ⚠⚠ zIndex 必須掛在**這一層**，不能只掛在裡面的 Sider 上。
             *
             * position:sticky **自己就會建立一個堆疊環境**（不論 z-index 是不是 auto）。
             * 一旦建立，裡面 Sider 的 z-index:100 就只在這個容器「內部」有意義，
             * 而容器自己若是 z-index:auto（＝0），整個側邊欄就會被頁面內那些
             * z-index:40 的 sticky 儲存格蓋過去 —— 2026-08-15 改成 sticky 時
             * 就會出現「頁面內容蓋住選單」。
             *
             * 數值沿用既有約定（見 index.css 的 z-index 說明）：
             * 頁面內容最高 40、側邊欄 100、浮層 900/910、antd Modal 1000+。
             */
            zIndex: 100,
            flex: `0 0 ${collapsed ? 64 : 220}px`,
            width: collapsed ? 64 : 220,
            transition: 'flex-basis .2s, width .2s',
          }}
          onMouseEnter={() => { if (collapsed) setHovering(true) }}
          onMouseLeave={() => setHovering(false)}
        >
          {/* trigger={null}：不要 antd 內建的底部橫桿，改用 Header 的漢堡
              （與行動版同位置、同圖示，換裝置時操作一致） */}
          {/* ⚠ zIndex 100：頁面內容的最高層是 40，
              浮起的選單必須高於它，否則頁面會蓋住選單。
              仍低於浮層 900/910 與 antd 的 Modal/Drawer 1000+ ——
              那些是「正在進行中的操作」，不該被側邊欄蓋掉。 */}
          <Layout.Sider theme="dark" width={220} collapsedWidth={64} trigger={null}
            collapsed={railMode}
            style={{
              position: 'absolute', insetInlineStart: 0, top: 0, height: '100%', zIndex: 100,
              // 配合下面的固定寬度容器：動畫期間把超出的部分裁掉
              // （彈出子選單是 portal 到 body 的，不會被這裡裁到）
              overflow: 'hidden',
            }}>
            {/*
              * ⚠ 內容一律用「最終寬度」排版，再由 Sider 裁切著露出來。
              *
              * 少了這層，滑入時會看到選單**擠成一團再攤開**：Menu 在 collapsed
              * 變 false 的那一刻就切成完整模式（帶文字），但 Sider 的寬度還在
              * 64→220 的動畫途中，文字被塞在 64px 裡換行，等寬度長完才復原。
              * 固定寬度後 Menu 從頭到尾都以 220 排版，動畫只是把它揭開。
              */}
            <div style={{
              width: railMode ? 64 : 220,
              height: '100%', display: 'flex', flexDirection: 'column',
            }}>
              {brand}
              {/*
                * 選單自己捲，品牌列固定不動（2026-08-15）。
                *
                * 三個屬性缺一不可：
                *   minHeight: 0  —— flex 子項預設**不會**縮到比內容矮，
                *                    少了它 overflowY:auto 永遠不觸發（flexbox 捲動最常見的坑）
                *   overflowX: 'hidden' —— 保留原本 overflow:hidden 的橫向裁切，
                *                    收合動畫要靠它把 220px 寬的內容裁在 64px 裡
                *   height: '100%'（上一層）—— 可以解析，因為 antd 的
                *                    .ant-layout-sider-children 本身就是 height:100%
                */}
              <div className="admin-nav-scroll"
                style={{ flex: 1, minHeight: 0, overflowY: 'auto', overflowX: 'hidden' }}>
                {navContent}
              </div>
            </div>
          </Layout.Sider>
        </div>
      )}

      {isMobile && (
        <Drawer
          placement="left" width={240} open={navOpen} onClose={() => setNavOpen(false)}
          closable={false} styles={{ body: { padding: 0, background: 'var(--c-sider-bg)' } }}
          classNames={{ content: 'admin-nav-drawer' }}
        >
          {brand}
          {navContent}
        </Drawer>
      )}

      <Layout>
        {/* ⚠ antd 的 Header 預設 height/lineHeight 都是 64px，兩個都要覆寫，
            只改 height 的話內容會被 64px 的行高推出框外。 */}
        <Layout.Header style={{
          background: '#fff', display: 'flex', alignItems: 'center',
          justifyContent: 'space-between', padding: isMobile ? '0 12px' : '0 24px',
          borderBottom: '1px solid var(--c-border)',
          height: 48, lineHeight: '48px',
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
            {/* 兩種裝置都顯示，但做的事不同：行動版開關抽屜、桌機收合成圖示軌 */}
            <Button type="text" icon={<MenuOutlined />}
              aria-label={isMobile ? '開關選單' : collapsed ? '展開選單' : '收合選單'}
              aria-expanded={isMobile ? navOpen : !collapsed}
              onClick={isMobile ? () => setNavOpen(o => !o) : toggleCollapsed} />
            {/* 行動版寬度有限，只放最後一層（站名在抽屜的品牌區已經有了，
                顯示所在頁面比再寫一次站名有用）。 */}
            {isMobile ? (
              <span style={{
                fontWeight: 700, color: 'var(--c-text)',
                overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
              }}>
                {trail[trail.length - 1] ?? 'Lobsang'}
              </span>
            ) : (
              <Breadcrumb
                style={{ fontSize: 'var(--fs-md)', lineHeight: 'normal' }}
                /* ⚠ 用 router 的 Link，不要自己寫 <a href="/"> ——
                   basename 是 /admin，寫死的 href 在中鍵開新分頁時會落到站台根目錄。 */
                items={[
                  { title: trail.length ? <Link to="/">首頁</Link> : '首頁' },
                  ...trail.map(name => ({ title: name })),
                ]}
              />
            )}
          </div>

          <Dropdown
            menu={{
              items: userMenu,
              onClick: ({ key }) => {
                if (key === 'logout') void logout()
                if (key === 'password') setPwdOpen(true)
              },
            }}
          >
            <span style={{ cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 8 }}>
              <Avatar size="small" style={{ background: 'var(--c-primary)' }}>{user?.name?.[0]}</Avatar>
              <span>{user?.name}</span>
            </span>
          </Dropdown>
        </Layout.Header>

        <Layout.Content className="admin-content">
          <Outlet />
        </Layout.Content>

        <Layout.Footer style={{ textAlign: 'center', color: 'var(--c-text-muted)', fontSize: 'var(--fs-sm)' }}>
          Lobsang
        </Layout.Footer>
      </Layout>

      {/* ⚠ 掛在最外層而不是 Header 裡 —— 強制模式要蓋住整個畫面，
          放在 Header 內會被版面的層級關係限制住 */}
      <ChangePasswordModal
        open={pwdOpen}
        forced={mustChange}
        onClose={() => setPwdOpen(false)}
      />
    </Layout>
  )
}
