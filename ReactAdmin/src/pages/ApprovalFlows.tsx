import { useCallback, useEffect, useState } from 'react'
import {
  Card, Table, Button, Space, Tag, Select, Modal, Form, Input, InputNumber,
  Switch, message, Alert, Typography, Popconfirm, Empty, Tooltip,
} from 'antd'
import { PlusOutlined, EditOutlined, DeleteOutlined, ReloadOutlined, ScissorOutlined } from '@ant-design/icons'
import { api, ApiError } from '../api'
import Forbidden from '../components/Forbidden'

/**
 * 簽核流程設定。
 *
 * ⚠⚠ 這一頁改的是**誰有權簽誰的單**，敏感度等同權限設定。
 *
 * ⭐ 改流程**不影響已經送出的單** —— 送簽當下就把簽核人寫死在單上（快照）。
 *   這是刻意的：組織或流程異動不該回頭改變在途單據的簽核人，否則稽核解釋不了。
 *
 * ── ⭐⭐ 級距為什麼用「未滿」而不是「以下」 ─────────────────────
 * 寫成 0~3 與 4~10，中間會有縫：**3.5 天兩邊都掉不進去**，
 * 而畫面只會說「查不到適用流程」，看不懂。
 * 改成「0 起、未滿 3」與「3 以上」，任何小數都不會漏，
 * 而且「未滿三天」正好就是講話的方式。
 */

const KIND_LABEL: Record<string, string> = {
  None: '無條件', Days: '天數', Hours: '時數', Amount: '金額',
}
const KIND_UNIT: Record<string, string> = { Days: '天', Hours: '小時', Amount: '元' }

const STEP_LABEL: Record<string, string> = {
  Role: '指定角色', ManagerChain: '直屬主管', SpecificUser: '指定人員',
  Counterpart: '對調的另一方',
}

/*
 * ⚠⚠ 「對調的另一方」**不可以出現在下拉選項裡**。
 *
 * 它不是一種可以挑的指派方式，是調班的**合意**本身：
 *   拿掉它 ＝ 不經當事人同意就能換他的班
 *   加到別的單據上 ＝ 那些單沒有對造，引擎會整關略過，默默少一關
 *
 * ⭐ 2026-08-23 業主回報：那一關本來也是一般下拉，**改掉之後選不回來**——
 *   因為選項清單裡根本沒有它。存下去就永久失去合意關。
 * ⇒ 改成整關鎖住：顯示得出來、改不動、也刪不掉。後端另有一道擋（不是只靠畫面）。
 */
const PICKABLE_STEPS = ['Role', 'ManagerChain', 'SpecificUser'] as const

interface Step {
  id?: number; stepOrder: number; stepType: string
  roleId: number | null; roleName?: string | null
  managerLevel: number | null
  specificUserId: number | null; specificUserName?: string | null
  /** 簽核方式（2026-10-09）：Default／Any（任一位）／All（會簽，全部都要簽、沒有順序） */
  signMode?: string
}
interface Flow {
  id: number; conditionKind: string
  minValue: number; maxValue: number | null
  name: string | null; isActive: boolean; sortOrder: number
  steps: Step[]
}
interface FormDef {
  id: number; code: string; name: string; category: string | null; flowCount: number
  /**
   * ⭐ 這張表單送簽時**有沒有數字可以比**。
   * 由後端決定（例：有天數、時數、金額欄位的表單）。
   * ⚠ 沒有數字卻設了級距，結果是永遠只走同一條 —— 一個不會生效的設定。
   *   所以那些表單直接不顯示分流相關的東西。
   */
  supportsCondition: boolean
  /** 這張表單可以依哪幾種數字分流（例：天數、時數、金額） */
  supportedKinds: string[]
}
interface Options {
  roles: { id: number; name: string }[]
  users: { id: number; name: string; title: string | null }[]
}

/** 把級距講成人話 */
function rangeText(f: Pick<Flow, 'conditionKind' | 'minValue' | 'maxValue'>) {
  if (f.conditionKind === 'None') return '永遠適用'
  const u = KIND_UNIT[f.conditionKind] ?? ''
  if (f.minValue === 0 && f.maxValue != null) return `未滿 ${f.maxValue} ${u}`
  if (f.maxValue == null) return `${f.minValue} ${u}以上`
  return `${f.minValue} ${u}起，未滿 ${f.maxValue} ${u}`
}

/** 表單下拉依表單分類分組；沒有分類的放「其他」 */
function groupForms(forms: FormDef[]) {
  const groups = new Map<string, { value: number; label: string }[]>()
  for (const f of forms) {
    const k = f.category || '其他'
    if (!groups.has(k)) groups.set(k, [])
    groups.get(k)!.push({ value: f.id, label: `${f.name}（${f.flowCount} 條）` })
  }
  return [...groups].map(([label, options]) => ({ label, title: label, options }))
}

export default function ApprovalFlows() {
  const [forms, setForms] = useState<FormDef[]>([])
  const [formId, setFormId] = useState<number>()
  const [flows, setFlows] = useState<Flow[]>([])
  const [opts, setOpts] = useState<Options>({ roles: [], users: [] })
  const [loading, setLoading] = useState(false)
  const [denied, setDenied] = useState(false)
  const [editing, setEditing] = useState<Flow | 'new' | null>(null)
  const [form] = Form.useForm()
  const kind = Form.useWatch('conditionKind', form)
  const [splitting, setSplitting] = useState<Flow | null>(null)
  const [splitForm] = Form.useForm()

  useEffect(() => {
    api.get<FormDef[]>('/api/admin/approval-flows/forms')
      .then(r => { setForms(r); if (r.length && formId === undefined) setFormId(r[0].id) })
      .catch(e => { if (e instanceof ApiError && e.status === 403) setDenied(true) })
    api.get<Options>('/api/admin/approval-flows/options').then(setOpts).catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const load = useCallback(() => {
    if (!formId) return
    setLoading(true)
    api.get<Flow[]>(`/api/admin/approval-flows?formDefinitionId=${formId}`)
      .then(setFlows)
      .catch(e => message.error(e instanceof ApiError ? e.message : '載入失敗'))
      .finally(() => setLoading(false))
  }, [formId])
  useEffect(() => { load() }, [load])

  const open = (f: Flow | 'new') => {
    setEditing(f)
    form.setFieldsValue(f === 'new'
      ? {
          conditionKind: flows.some(x => x.conditionKind !== 'None') ? (kinds[0] ?? 'None') : 'None',
          minValue: 0, maxValue: null, name: '', isActive: true,
          sortOrder: flows.length + 1,
          steps: [{ stepOrder: 1, stepType: 'ManagerChain', managerLevel: 1 }],
        }
      : { ...f, steps: f.steps.map(s => ({ ...s })) })
  }

  const save = async () => {
    const v = await form.validateFields()
    const body = {
      formDefinitionId: formId,
      conditionKind: v.conditionKind,
      minValue: v.conditionKind === 'None' ? 0 : (v.minValue ?? 0),
      maxValue: v.conditionKind === 'None' ? null : (v.maxValue ?? null),
      name: v.name, isActive: v.isActive, sortOrder: v.sortOrder ?? 1,
      steps: (v.steps ?? []).map((s: Step, i: number) => ({ ...s, stepOrder: i + 1 })),
    }
    try {
      if (editing === 'new') await api.post('/api/admin/approval-flows', body)
      else if (editing) await api.put(`/api/admin/approval-flows/${editing.id}`, body)
      message.success('已儲存')
      setEditing(null)
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '儲存失敗')
    }
  }

  /*
   * ⭐⭐ 建立級距的正路是「切一刀」，不是「再加一條」。
   *
   * 級距不是各自獨立的區間，是**數線上的分界點** ——
   * 「三天以上要多一關」＝ 在 3 這裡切開。
   * 這樣做，級距在建構上就不可能有縫或重疊。
   * （⚠ 2026-08-23 實測發現：只用「新增」做不到從單一流程變成兩段，
   *   加第二段說第一段還是無條件、改第一段說最後一段必須不限，互相卡死。）
   */
  const doSplit = async () => {
    if (!splitting) return
    const v = await splitForm.validateFields()
    try {
      await api.post(`/api/admin/approval-flows/${splitting.id}/split`, {
        atValue: v.atValue, conditionKind: v.conditionKind,
        lowerName: v.lowerName || null, upperName: v.upperName || null,
      })
      message.success('已切成兩段，請接著調整各段的關卡')
      setSplitting(null)
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '切割失敗')
    }
  }

  const remove = async (f: Flow) => {
    try {
      await api.del(`/api/admin/approval-flows/${f.id}`)
      message.success('已刪除')
      load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '刪除失敗')
    }
  }

  if (denied) return <Forbidden />

  const conditional = flows.filter(f => f.conditionKind !== 'None' && f.isActive)
  const canSplit = forms.find(f => f.id === formId)?.supportsCondition ?? false
  // ⚠ 只列這張表單拿得出來的數字
  const kinds = forms.find(f => f.id === formId)?.supportedKinds ?? []
  const kindOptions = kinds.map(k => ({ value: k, label: KIND_LABEL[k] ?? k }))

  return (
    <Space direction="vertical" size={12} style={{ width: '100%' }}>
      <Alert
        type="info" showIcon
        message="改流程不會影響已經送出的單"
        description={
          <div style={{ lineHeight: 1.9 }}>
            送簽的當下就把簽核人寫在單子上了，之後改這裡不會回頭改變在途的單 ——
            這是刻意的，否則「送出時該你簽、現在變別人」會說不清楚。
            <br />
            ⭐ 改動<b>從下一張新單開始生效</b>。
          </div>
        } />

      <Card
        size="small"
        title={
          <Space wrap>
            <span>簽核流程</span>
            <Select
              style={{ width: 300 }} value={formId} onChange={setFormId}
              showSearch optionFilterProp="label" listHeight={420}
              options={groupForms(forms)} />
          </Space>
        }
        extra={
          <Space>
            <Button icon={<ReloadOutlined />} onClick={load} />
            {/*
              * ⚠⚠ 已經有流程時**不顯示「新增流程」**。
              *
              * 那顆按鈕在幾乎所有情況下都是錯的路：要依天數／時數分流，
              * 正確作法是把現有的那段「切成兩段」。硬用新增只會被驗證擋下來
              * （無條件與有條件不能混、級距不能有縫），
              * 而使用者看到的只是一連串看不懂的錯誤訊息。
              * ⭐ 判準：**畫面不該把最顯眼的按鈕留給幾乎永遠錯的那條路。**
              */}
            {flows.length === 0 && (
              <Button type="primary" icon={<PlusOutlined />} onClick={() => open('new')}>
                建立簽核流程
              </Button>
            )}
          </Space>
        }>

        {conditional.length > 0 && (
          <Alert
            type="warning" showIcon style={{ marginBottom: 12 }}
            message="這張表單目前依條件分流"
            description={
              <span>
                送出的單會依<b>{KIND_LABEL[conditional[0].conditionKind]}</b>落在哪一段，走對應的流程。
                ⚠ 級距不能有缺口，否則落在缺口的單會送不出去 —— 存檔時會擋。
              </span>
            } />
        )}

        {/*
          * ⭐ 只有一條流程時，主動指路 —— 「要分流該按哪裡」不該讓人自己猜。
          *   （原本最顯眼的是「新增流程」，但那條路走不通。）
          */}
        {canSplit && flows.length === 1 && conditional.length === 0 && (
          <Alert
            type="info" showIcon style={{ marginBottom: 12 }}
            message="想讓「請比較多天」多一關嗎？"
            description={
              <span>
                按下面那一列的 <b>切成兩段</b>，填幾天切開就好。
                例：填 <b>5</b> → 變成「未滿五天」和「五天以上」兩段，
                再去編輯「五天以上」加關卡。
              </span>
            } />
        )}

        <Table<Flow>
          rowKey="id" loading={loading} dataSource={flows} pagination={false}
          locale={{ emptyText: <Empty description="這張表單還沒有簽核流程" /> }}
          columns={[
            {
              title: '適用範圍', width: 220,
              render: (_, f) => (
                <Space direction="vertical" size={0}>
                  <b>{f.name || rangeText(f)}</b>
                  <span style={{ color: 'var(--c-text-muted)', fontSize: 'var(--fs-sm)' }}>
                    {KIND_LABEL[f.conditionKind]}　{rangeText(f)}
                  </span>
                </Space>
              ),
            },
            {
              title: '關卡', render: (_, f) => (
                <Space wrap size={4}>
                  {f.steps.map((s, i) => (
                    <span key={s.id ?? i}>
                      {i > 0 && <span style={{ color: 'var(--c-text-muted)' }}>→ </span>}
                      <Tooltip title={STEP_LABEL[s.stepType]}>
                        {/*
                          * ⚠ 每加一種關卡型別，這裡也要跟著加 ——
                          *   漏掉的話會掉進最後那個 else，顯示成「（角色）」，
                          *   看起來像是設定壞了。（2026-08-23 業主就是先看到這個。）
                          */}
                        <Tag color={s.stepType === 'Counterpart' ? 'gold'
                          : s.stepType === 'ManagerChain' ? 'blue'
                          : s.stepType === 'SpecificUser' ? 'purple' : 'default'}>
                          {s.stepType === 'Counterpart'
                            ? '對調的另一方'
                            : s.stepType === 'ManagerChain'
                              ? `第 ${s.managerLevel ?? 1} 層主管`
                              : s.stepType === 'SpecificUser'
                                ? (s.specificUserName ?? '（指定人員）')
                                : (s.roleName ?? '（角色）')}
                          {s.signMode === 'All' ? '（會簽）' : s.signMode === 'Any' ? '（任一位）' : ''}
                        </Tag>
                      </Tooltip>
                    </span>
                  ))}
                </Space>
              ),
            },
            {
              title: '狀態', width: 80,
              render: (_, f) => f.isActive
                ? <Tag color="green">啟用</Tag>
                : <Tag>停用</Tag>,
            },
            {
              title: '操作', width: 150,
              render: (_, f) => (
                <Space>
                  <Button size="small" icon={<EditOutlined />} onClick={() => open(f)}>編輯</Button>
                  {canSplit && f.maxValue == null && (
                    <Tooltip title="在某個數值切一刀，分成兩段不同的簽核流程">
                      {/* ⭐ 這才是分流的正路，做成主要按鈕 */}
                      <Button size="small" type="primary" ghost icon={<ScissorOutlined />}
                        onClick={() => {
                          setSplitting(f)
                          splitForm.setFieldsValue({
                            conditionKind: f.conditionKind === 'None' ? (kinds[0] ?? 'Days') : f.conditionKind,
                            atValue: undefined, lowerName: '', upperName: '',
                          })
                        }}>切成兩段</Button>
                    </Tooltip>
                  )}
                  <Popconfirm title="刪除這條流程？" onConfirm={() => remove(f)}>
                    <Button size="small" danger icon={<DeleteOutlined />} />
                  </Popconfirm>
                </Space>
              ),
            },
          ]} />
      </Card>

      <Modal
        open={!!editing} width={760}
        title={editing === 'new' ? '新增簽核流程' : '編輯簽核流程'}
        onCancel={() => setEditing(null)} onOk={save} okText="儲存">
        <Form form={form} layout="vertical">
          <Form.Item name="name" label="這條流程叫什麼"
            tooltip="只是顯示用，不影響判斷。留空會自動用範圍描述">
            <Input placeholder="例：未滿三天" />
          </Form.Item>

          {/*
            ⚠ 排序沒有畫面可改，但**一定要註冊** —— 否則 validateFields() 不會回它，
              儲存時 `v.sortOrder ?? 1` 會把級距第二段的排序**悄悄改回 1**，
              而引擎是照 SortOrder 挑流程的。與上面那個 Counterpart 是同一種錯。
          */}
          <Form.Item name="sortOrder" noStyle hidden><InputNumber /></Form.Item>

          <Space size={10} align="start" wrap>
            {/*
              * ⚠ 這張表單送簽時沒有數字可比時，連「依什麼分流」都不顯示 ——
              *   給了選項卻怎麼設都不會生效，比不給更糟。
              */}
            {canSplit ? (
              <Form.Item name="conditionKind" label="依什麼分流" rules={[{ required: true }]}>
                <Select style={{ width: 140 }}
                  options={[{ value: 'None', label: KIND_LABEL.None }, ...kindOptions]} />
              </Form.Item>
            ) : (
              <Form.Item name="conditionKind" hidden><Input /></Form.Item>
            )}
            {kind !== 'None' && (
              <>
                <Form.Item name="minValue" label="從多少起（含）" rules={[{ required: true }]}>
                  <InputNumber min={0} style={{ width: 140 }} addonAfter={KIND_UNIT[kind] ?? ''} />
                </Form.Item>
                <Form.Item name="maxValue" label="未滿多少"
                  tooltip="留空 = 以上不限。⚠ 一組級距只能有一段留空，而且要是最後一段">
                  <InputNumber min={0} style={{ width: 150 }} addonAfter={KIND_UNIT[kind] ?? ''}
                    placeholder="留空＝不限" />
                </Form.Item>
              </>
            )}
            <Form.Item name="isActive" label="狀態" valuePropName="checked">
              <Switch checkedChildren="啟用" unCheckedChildren="停用" />
            </Form.Item>
          </Space>

          {kind !== 'None' && (
            <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 12 }}>
              ⭐ 上限是<b>不含</b>的：填 3 代表「未滿 3」，剛好 3 會走下一段。
              這樣 2.5 才不會兩邊都掉不進去。
            </Typography.Text>
          )}

          <Typography.Text strong>簽核關卡（由上而下依序簽）</Typography.Text>
          <Form.List name="steps">
            {(fields, { add, remove: rm }) => (
              <div style={{ marginTop: 8 }}>
                {fields.map((field, idx) => (
                  <Space key={field.key} align="start" wrap style={{ marginBottom: 8 }}>
                    <Tag>第 {idx + 1} 關</Tag>
                    <Form.Item noStyle shouldUpdate>
                      {() => {
                        const t0 = form.getFieldValue(['steps', field.name, 'stepType'])
                        // ⭐ 合意關：只顯示，不給改（理由見 PICKABLE_STEPS 上方註解）
                        if (t0 === 'Counterpart') return (
                          <>
                            {/*
                              ⚠⚠ 這個 hidden 欄位**不能省**。
                                 antd 的 validateFields() 只會回「**有註冊過**的欄位」——
                                 這一關原本只畫一個 Tag，沒有任何 Form.Item，
                                 於是整關在送出時**整個消失**，後端收到的第一關沒有 stepType，
                                 被 model binding 當成預設值 Role ⇒ 一律被
                                 「調班一定要有『對調的另一方』這一關」擋下來。
                              ⚠ 而這一關在畫面上**刻意不給改也不給刪**
                                 ⇒ 調班單的簽核流程一旦點進編輯就**再也存不了**，
                                   使用者也沒有任何辦法自行排除。2026-09-13 業主回報。
                              ⭐ 同一份檔案的 conditionKind 早就用這個寫法（不顯示時也要註冊）。
                            */}
                            <Form.Item name={[field.name, 'stepType']} noStyle hidden><Input /></Form.Item>
                            <Tooltip title="調班一定要當事人自己同意，這一關不能改也不能刪">
                              <Tag color="gold">對調的另一方（固定）</Tag>
                            </Tooltip>
                          </>
                        )
                        return (
                          <Form.Item name={[field.name, 'stepType']} noStyle>
                            <Select style={{ width: 130 }}
                              options={PICKABLE_STEPS.map(v => ({ value: v, label: STEP_LABEL[v] }))} />
                          </Form.Item>
                        )
                      }}
                    </Form.Item>
                    <Form.Item noStyle shouldUpdate>
                      {() => {
                        const t = form.getFieldValue(['steps', field.name, 'stepType'])
                        if (t === 'Counterpart') return null
                        if (t === 'ManagerChain') return (
                          <Form.Item name={[field.name, 'managerLevel']} noStyle>
                            <InputNumber min={1} max={5} style={{ width: 150 }}
                              addonBefore="往上第" addonAfter="層" />
                          </Form.Item>
                        )
                        if (t === 'SpecificUser') return (
                          <Form.Item name={[field.name, 'specificUserId']} noStyle>
                            <Select style={{ width: 200 }} placeholder="選一位" showSearch
                              optionFilterProp="label"
                              options={opts.users.map(u => ({
                                value: u.id, label: u.title ? `${u.name}（${u.title}）` : u.name,
                              }))} />
                          </Form.Item>
                        )
                        return (
                          <Form.Item name={[field.name, 'roleId']} noStyle>
                            <Select style={{ width: 200 }} placeholder="選一個角色"
                              options={opts.roles.map(r => ({ value: r.id, label: r.name }))} />
                          </Form.Item>
                        )
                      }}
                    </Form.Item>
                    <Form.Item noStyle shouldUpdate>
                      {() => {
                        /* ⭐ 簽核方式（2026-10-09 業主定案）：只有主管、角色關卡有。
                         * ⚠ 角色關卡本來就是「有這個角色的任一位都能簽」⇒ 只多一個「會簽」。 */
                        const t = form.getFieldValue(['steps', field.name, 'stepType'])
                        if (t !== 'ManagerChain' && t !== 'Role') return null
                        const options = t === 'ManagerChain'
                          ? [
                              { value: 'Default', label: '指定一位（送單的人選）' },
                              { value: 'Any', label: '任一位簽即可' },
                              { value: 'All', label: '會簽（全部都要簽）' },
                            ]
                          : [
                              { value: 'Default', label: '任一位簽即可' },
                              { value: 'All', label: '會簽（所有有此角色者都要簽）' },
                            ]
                        return (
                          <Tooltip title={t === 'ManagerChain'
                            ? '這一層有兩位以上主管時才有差別；只有一位主管時一律由他簽'
                            : '會簽＝所有有這個角色的在職人員都要簽，沒有順序；名單在送出當下固定'}>
                            <Form.Item name={[field.name, 'signMode']} noStyle initialValue="Default">
                              <Select style={{ width: 190 }} popupMatchSelectWidth={false} options={options} />
                            </Form.Item>
                          </Tooltip>
                        )
                      }}
                    </Form.Item>
                    <Form.Item noStyle shouldUpdate>
                      {() => {
                        const t = form.getFieldValue(['steps', field.name, 'stepType'])
                        return (
                          <Button size="small" danger onClick={() => rm(field.name)}
                            disabled={fields.length <= 1 || t === 'Counterpart'}>移除</Button>
                        )
                      }}
                    </Form.Item>
                  </Space>
                ))}
                <Button size="small" icon={<PlusOutlined />}
                  onClick={() => add({ stepType: 'Role', managerLevel: 1 })}>
                  加一關
                </Button>
              </div>
            )}
          </Form.List>

          <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginTop: 12, lineHeight: 1.9 }}>
            ⚠ <b>直屬主管</b>找不到人時，會退回這一關設定的角色（若有），
            所以組織編制沒設主管的單位不會卡住。
            <br />
            ⚠ <b>指定角色</b>是「有這個角色的人都能簽」，<b>不分單位</b>。
            各單位共用的表單請用<b>主管</b>關卡，
            否則別的單位的單也會送到這個角色手上。
            <br />
            ⭐ <b>會簽</b>：名單上的人全部都要簽、沒有順序，最後一位簽完才往下一關；任一人退件＝整張退件。
            <b>任一位</b>：名單上任何一位簽了就過。
            <br />
            ⚠⚠ <b>指定人員</b>只能選啟用中的人。那個人離職時，
            帳號管理會擋著不讓停用，直到這裡改掉為止。
          </Typography.Text>
        </Form>
      </Modal>

      <Modal
        open={!!splitting} width={560} title="把這一段切成兩段"
        onCancel={() => setSplitting(null)} onOk={doSplit} okText="切開">
        <Alert
          type="info" showIcon style={{ marginBottom: 14 }}
          message="要分流，就在數線上切一刀"
          description={
            <div style={{ lineHeight: 1.9 }}>
              例：想讓「三天以上」多一關主管，就在 <b>3</b> 這裡切開 ——
              系統會變成「未滿 3 天」和「3 天以上」兩段。
              <br />
              ⭐ 新的那一段會<b>先沿用現在的關卡</b>，切完再去編輯加關卡就好。
              <br />
              ⚠ 剛好 3 天算「3 天以上」那一段。
            </div>
          } />
        <Form form={splitForm} layout="vertical">
          <Space size={10} align="start" wrap>
            <Form.Item name="conditionKind" label="依什麼分流" rules={[{ required: true }]}>
              <Select style={{ width: 130 }}
                options={kindOptions} />
            </Form.Item>
            <Form.Item name="atValue" label="在多少切開"
              rules={[{ required: true, message: '請填切割點' }]}>
              <InputNumber min={0} style={{ width: 150 }} placeholder="例：3" />
            </Form.Item>
          </Space>
          <Space size={10} wrap>
            <Form.Item name="lowerName" label="下面那段叫什麼">
              <Input style={{ width: 180 }} placeholder="例：未滿三天" />
            </Form.Item>
            <Form.Item name="upperName" label="上面那段叫什麼">
              <Input style={{ width: 180 }} placeholder="例：三天以上" />
            </Form.Item>
          </Space>
        </Form>
      </Modal>
    </Space>
  )
}
