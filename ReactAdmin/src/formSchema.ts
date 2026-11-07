/**
 * 表單引擎的結構定義（工作記錄 Records.tsx 使用）。
 *
 *   - section.type = 'rows'：可新增多列的表格（明細、追蹤事項…），內容存成陣列
 *   - section.showWhen：依某欄的值才顯示
 * ⚠ 後端不驗證內容結構（ContentJson 原樣存），所以舊記錄缺欄位是正常的，畫面都要容忍。
 */

export interface FieldDef {
  key: string
  label: string
  /** photos：照片格（直接上傳成這筆記錄的附件，依欄位分開存） */
  type: 'text' | 'textarea' | 'number' | 'date' | 'select' | 'checklist' | 'photos'
  options?: string[]
  required?: boolean
  /** 欄位說明（顯示在欄位上方，可多行） */
  help?: string
  placeholder?: string
  /** 自動計算；有設定就不讓人手打 */
  compute?: Compute
}

/**
 * bmi：體重 ÷ 身高(公尺)²（一列資料內）
 * running：明細的「餘額」＝起始值＋逐列累加收入－支出（rows 欄位用）
 * copy：等於另一欄；sum：某個明細表某一欄的合計；calc：加總 plus 減掉 minus
 */
export type Compute =
  | { kind: 'bmi'; height: string; weight: string }
  | { kind: 'running'; start: string; plus: string; minus: string }
  | { kind: 'copy'; from: string }
  | { kind: 'sum'; rows: string; col: string }
  | { kind: 'calc'; plus: string[]; minus: string[] }

const num = (v: unknown) => (v === null || v === undefined || v === '' ? null : Number.isFinite(Number(v)) ? Number(v) : null)

/** 依 compute 設定補上自動計算的欄位（一列資料） */
export const applyCompute = (cols: FieldDef[], row: Record<string, unknown>) => {
  const out = { ...row }
  for (const c of cols) {
    if (c.compute?.kind !== 'bmi') continue
    const h = Number(out[c.compute.height]), w = Number(out[c.compute.weight])
    out[c.key] = h > 0 && w > 0 ? Math.round((w / ((h / 100) ** 2)) * 10) / 10 : null
  }
  return out
}

/**
 * 整張表的自動計算（copy → sum → running → calc 依序算）。回傳新的內容；沒有要算的就原樣回傳。
 * ⚠ 存檔前、列印時都要套一次 —— 列印頁拿到的是存下來的內容，舊記錄可能還沒算過。
 */
export function deriveContent(schema: FormSchema, content: Content): Content {
  const out: Content = { ...content }
  const fields = schema.sections.flatMap(s => s.fields ?? [])
  let changed = false
  const set = (k: string, v: unknown) => { if (out[k] !== v) { out[k] = v; changed = true } }
  for (const f of fields) if (f.compute?.kind === 'copy') set(f.key, num(out[f.compute.from]))
  for (const f of fields) if (f.compute?.kind === 'sum') {
    const c = f.compute
    const rows = (out[c.rows] as RowData[] | undefined) ?? []
    set(f.key, rows.length ? rows.reduce((t, r) => t + (num(r[c.col]) ?? 0), 0) : null)
  }
  for (const s of schema.sections) {
    if (s.type !== 'rows' || !s.key) continue
    const run = (s.columns ?? []).find(c => c.compute?.kind === 'running')
    if (!run || run.compute?.kind !== 'running') continue
    const rc = run.compute
    let bal = num(out[rc.start]) ?? 0
    const rows = ((out[s.key] as RowData[] | undefined) ?? []).map(r => {
      bal += (num(r[rc.plus]) ?? 0) - (num(r[rc.minus]) ?? 0)
      return r[run.key] === bal ? r : { ...r, [run.key]: bal }
    })
    if (rows.some((r, i) => r !== (out[s.key!] as RowData[])[i])) { out[s.key] = rows; changed = true }
  }
  for (const f of fields) if (f.compute?.kind === 'calc') {
    const c = f.compute
    const vals = [...c.plus, ...c.minus].map(k => num(out[k]))
    set(f.key, vals.every(v => v === null) ? null
      : c.plus.reduce((t, k) => t + (num(out[k]) ?? 0), 0) - c.minus.reduce((t, k) => t + (num(out[k]) ?? 0), 0))
  }
  return changed ? out : content
}

export interface ShowWhen { key: string; in: string[] }

export interface SectionDef {
  title: string
  type?: 'rows'
  key?: string
  fields?: FieldDef[]
  columns?: FieldDef[]
  showWhen?: ShowWhen
  /** rows：新增按鈕的字（例：新增一筆） */
  addLabel?: string
  help?: string
}

export interface FormSchema {
  sections: SectionDef[]
}

export type Content = Record<string, unknown>
export type RowData = Record<string, unknown>

/** 依 showWhen 判斷要不要顯示（沒有條件就顯示） */
export const visibleBy = (when: ShowWhen | undefined, content: Content) =>
  !when || when.in.includes(String(content[when.key] ?? ''))

/** 必填但沒填的欄位名稱（只看顯示中的區塊；照片、多列表格不檢查） */
export const missingRequired = (schema: FormSchema, content: Content) =>
  schema.sections
    .filter(s => !s.type && visibleBy(s.showWhen, content))
    .flatMap(s => s.fields ?? [])
    .filter(f => f.required && f.type !== 'photos')
    .filter(f => {
      const v = content[f.key]
      return v == null || v === '' || (Array.isArray(v) && v.length === 0)
    })
    .map(f => f.label)
