import type React from 'react'

/**
 * LINE（LIFF）頁面共用的樣式。
 *
 * ⚠ 全部用 inline style：這些頁不套後台的樣式表，避免為了一個表單
 *   把整包 CSS 拉進 LINE 的內建瀏覽器。
 * ⚠ 輸入框字級一定要 ≥16px —— 行動瀏覽器對小於 16px 的輸入框會**自動放大整頁**，
 *   使用者會以為畫面壞了。
 *
 * ⭐ 從 LineBind.tsx 搬出來共用（之後的 LINE 畫面也要用同一套）。
 *    ⚠ 不要在各頁各寫一份 —— 兩份樣式一定有一份先走樣，而走樣的症狀是
 *    「有一頁看起來不像同一個系統」，沒有人會為此開單。
 */
export const S: Record<string, React.CSSProperties> = {
  page: {
    minHeight: '100vh', background: '#f1f5f9', display: 'flex',
    alignItems: 'flex-start', justifyContent: 'center', padding: 16,
    fontFamily: '"Noto Sans TC","Microsoft JhengHei",system-ui,sans-serif',
  },
  card: {
    width: '100%', maxWidth: 420, background: '#fff', borderRadius: 14,
    padding: '26px 22px 22px', boxShadow: '0 6px 24px rgba(15,23,42,.10)',
  },
  brand: { fontSize: 12, fontWeight: 700, letterSpacing: 2, color: '#d97706' },
  title: { fontSize: 21, fontWeight: 800, margin: '6px 0 14px', color: '#0f172a' },
  hint: { fontSize: 14, lineHeight: 1.8, color: '#475569', margin: '0 0 16px' },
  small: { fontSize: 12, color: '#94a3b8', textAlign: 'center', marginTop: 14 },
  label: { display: 'flex', flexDirection: 'column', gap: 6, fontSize: 14, fontWeight: 600, color: '#334155', marginBottom: 14 },
  // ⚠ 字級一定要 ≥16px —— 行動瀏覽器對小於 16px 的輸入框會自動放大整頁
  input: { width: '100%', padding: '12px 12px', fontSize: 16, border: '1px solid #cbd5e1', borderRadius: 9, outline: 'none' },
  btn: { width: '100%', padding: 14, fontSize: 16, fontWeight: 700, color: '#fff', background: '#d97706', border: 'none', borderRadius: 9, cursor: 'pointer' },
  btnBusy: { opacity: .6 },
  btnGhost: { width: '100%', padding: 12, fontSize: 15, color: '#334155', background: '#fff', border: '1px solid #cbd5e1', borderRadius: 9, cursor: 'pointer' },
  alertErr: { background: '#fef2f2', border: '1px solid #fecaca', color: '#b91c1c', borderRadius: 9, padding: '11px 13px', fontSize: 14, lineHeight: 1.7, marginBottom: 14 },
  alertOk: { background: '#ecfdf5', border: '1px solid #a7f3d0', color: '#047857', borderRadius: 9, padding: '14px 13px', fontSize: 15, fontWeight: 700, textAlign: 'center', marginBottom: 14 },
  alertWarn: { background: '#fffbeb', border: '1px solid #fde68a', color: '#92400e', borderRadius: 9, padding: '14px 13px', fontSize: 15, fontWeight: 700, textAlign: 'center', marginBottom: 14 },
  btnDanger: { width: '100%', padding: 14, fontSize: 16, fontWeight: 700, color: '#fff', background: '#dc2626', border: 'none', borderRadius: 9, cursor: 'pointer' },
  who: { fontSize: 20, marginTop: 6, color: '#0f172a' },
}
