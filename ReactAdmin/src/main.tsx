import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { ConfigProvider } from 'antd'
import zhTW from 'antd/locale/zh_TW'
import dayjs from 'dayjs'
import customParseFormat from 'dayjs/plugin/customParseFormat'

/* ⚠⚠ dayjs 的「用指定格式解析字串」是**外掛**，不是內建。
 *   沒有它的話 `dayjs('09:00', 'HH:mm')` 回的是 **Invalid Date**，而且不會拋錯。
 * ⭐ 在進入點載入一次，全站的 dayjs(值, 格式) 就都正確了。 */
dayjs.extend(customParseFormat)
import Login from './pages/Login'
import AdminLayout from './layouts/AdminLayout'
import Welcome from './pages/Welcome'
import Users from './pages/Users'
import Roles from './pages/Roles'
import Menus from './pages/Menus'
import Records from './pages/Records'
import Books from './pages/Books'
import Products from './pages/Products'
import OrgUnits from './pages/OrgUnits'
import Delegations from './pages/Delegations'
import ApprovalFlows from './pages/ApprovalFlows'
import AuditLogs from './pages/AuditLogs'
import ErrorLogs from './pages/ErrorLogs'
import LineBind from './pages/LineBind'
import MyStepUpPassword from './pages/MyStepUpPassword'
import './index.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ConfigProvider
      locale={zhTW}
      /*
       * ⚠ 這裡的色碼**必須與 index.css 的 :root 一致** —— antd 的 theme token
       *   讀不到 CSS 變數（它要在執行期算出衍生色階，需要真正的值），
       *   所以這是全專案唯一一份重複。改色時兩邊都要動。
       *   語意來源在 index.css 的 token 區塊，那裡是單一真相。
       */
      theme={{
        token: {
          colorPrimary: '#d97706',
          colorError: '#dc2626',
          colorSuccess: '#059669',
          colorWarning: '#d97706',
          colorTextSecondary: '#64748b',
          colorBorder: '#e2e8f0',
          borderRadius: 4,

          /*
           * ⚠ **種子色給得好，不代表 antd 推導出的色階就好** —— 這兩行是實測後補的。
           *
           * antd 由 colorSuccess 推導整組綠色。`#059669`（emerald-600）偏深又不飽和，
           * 推出來的底色是 `#C1D6CC` 濁灰綠 —— 與其他三個狀態的淡底
           * （error #FFF2F0、processing #E6F4FF、warning #FFF7E6）明顯不同調，
           * 「已核准」「已變更」這類標籤看起來像是失效的灰標。
           * 同一組設定裡，琥珀推得很好、綠色沒有，所以只補這一個色系。
           *
           * 值取 emerald-50／200，與 colorSuccess 同色系且亮度對齊其餘三者。
           */
          colorSuccessBg: '#ecfdf5',
          colorSuccessBorder: '#a7f3d0',
        },
        components: { Form: { itemMarginBottom: 14 } },
      }}
    >
      <BrowserRouter basename="/admin">
        <Routes>
          <Route path="/login" element={<Login />} />
          {/* ⚠ 綁定頁刻意放在 AdminLayout **之外** —— 綁定的當下使用者還沒登入，
              包進後台框架會變成「要先登入才能綁定」的死結。
              LIFF Endpoint 設定為 https://<網域>/admin/line/bind */}
          <Route path="/line/bind" element={<LineBind />} />
          <Route path="/" element={<AdminLayout />}>
            <Route index element={<Welcome />} />
            <Route path="users" element={<Users />} />
            <Route path="roles" element={<Roles />} />
            <Route path="menus" element={<Menus />} />
            <Route path="org-units" element={<OrgUnits />} />
            <Route path="delegations" element={<Delegations />} />
            <Route path="approval-flows" element={<ApprovalFlows />} />
            <Route path="records" element={<Records />} />
            <Route path="books" element={<Books />} />
            {/* 商品（示意版 mock，2026-11-08）：選單由 DbSeeder 建立（書籍 → 商品） */}
            <Route path="products" element={<Products />} />
            <Route path="audit-logs" element={<AuditLogs />} />
            <Route path="error-logs" element={<ErrorLogs />} />
            <Route path="my/step-up-password" element={<MyStepUpPassword />} />
            {/* ⚠ 沒有對應頁面的選單路徑會被導回首頁 —— 新增選單時記得在這裡掛路由 */}
            <Route path="*" element={<Navigate to="/" replace />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </ConfigProvider>
  </StrictMode>,
)
