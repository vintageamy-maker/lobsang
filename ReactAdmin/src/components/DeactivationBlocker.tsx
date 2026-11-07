import { useCallback, useEffect, useState } from 'react'
import {
  Modal, Alert, Table, Tag, Space, Button, Select, Input,
  Typography, Spin, message,
} from 'antd'
import { Link } from 'react-router-dom'
import { api, ApiError } from '../api'

/**
 * 停用帳號前的攔截畫面。
 *
 * ── 為什麼要有這一頁 ─────────────────────────────────────────────
 * 送簽當下就把簽核人寫死在單上（快照），所以人一停用，掛在他身上的單
 * **不會自己脫困** —— 而且畫面上完全看不出異常，只是靜靜躺著沒人簽。
 *
 * ⚠⚠ **四類要一起清，不能只清第一類。**
 *   只把「在途的單」改派掉，下一張新單子還是會送到同一個人手上，
 *   因為他在組織編制上仍然是某個單位的主管。
 *   ⭐ 第一類是「現在卡住的」；第二、四類是「以後還會再卡」。
 *
 * ⚠ 第二～四類**這裡不代改** —— 那是組織編制與流程設定，
 *   在停用的流程裡順手改掉，等於用副作用改組織。這裡只指路。
 */

const VIEWER = '加簽檢視者'

interface PendingStep {
  docType: string; docId: number; docLabel: string; stepId: number
  round: number; stepOrder: number; stepName: string
  applicantName: string; submittedAt: string | null
}
interface UnitManager { orgUnitId: number; unitName: string; source: string; managerRowId: number | null }
interface Delegation { id: number; side: string; counterpartName: string; startDate: string; endDate: string }
interface SpecificStep { stepId: number; formName: string; stepOrder: number }

interface Impact {
  userId: number; name: string
  pendingSteps: PendingStep[]
  unitManagerOf: UnitManager[]
  delegations: Delegation[]
  specificSteps: SpecificStep[]
  total: number
  blockingCount: number
  blocked: boolean
}

const DOC_LABEL: Record<string, string> = {
  record: '工作記錄',
}

export default function DeactivationBlocker({ user, onCancel, onCleared }: {
  user: { id: number; name: string } | null
  onCancel: () => void
  /** 四類都清乾淨了，可以真的去停用 */
  onCleared: () => void
}) {
  const [impact, setImpact] = useState<Impact | null>(null)
  const [loading, setLoading] = useState(false)
  const [toUserId, setToUserId] = useState<number>()
  const [reason, setReason] = useState('原簽核人離職／停用')
  const [busy, setBusy] = useState(false)
  /*
   * ⚠ 候選人要**全機構在職的人**，不能只給呼叫端當下那一頁 ——
   *   帳號管理一頁十筆，要改派給第二頁的人就選不到，而且畫面上看不出為什麼。
   */
  const [people, setPeople] = useState<{ id: number; name: string; title: string | null }[]>([])

  const load = useCallback(async () => {
    if (!user) return
    setLoading(true)
    try {
      setImpact(await api.get<Impact>(`/api/admin/users/${user.id}/deactivation-impact`))
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '查詢失敗')
    } finally {
      setLoading(false)
    }
  }, [user])

  useEffect(() => { if (user) { setImpact(null); setToUserId(undefined); load() } }, [user, load])

  useEffect(() => {
    if (!user) return
    api.get<{ items: { id: number; name: string; title: string | null; isActive: boolean }[] }>(
      '/api/admin/users?pageSize=500&isActive=true')
      .then(r => setPeople((r.items ?? []).filter(u => u.isActive)))
      .catch(() => setPeople([]))
  }, [user])

  const reassignAll = async () => {
    if (!user || !toUserId) return
    if (!reason.trim()) { message.warning('請填寫改派原因'); return }
    setBusy(true)
    try {
      const res = await api.post<{ changed: number }>(
        `/api/admin/users/${user.id}/reassign-approvals`, { toUserId, reason: reason.trim() })
      message.success(`已改派 ${res.changed} 關`)
      await load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '改派失敗')
    } finally {
      setBusy(false)
    }
  }

  const clean = impact !== null && !impact.blocked
  const others = people.filter(c => c.id !== user?.id)

  return (
    <Modal
      open={!!user}
      title={`停用「${user?.name ?? ''}」前要先處理的事`}
      width={860}
      onCancel={onCancel}
      footer={
        <Space>
          <Button onClick={onCancel}>取消</Button>
          <Button onClick={load} loading={loading}>重新檢查</Button>
          <Button type="primary" danger disabled={!clean} onClick={onCleared}>
            {clean ? '確定停用' : '尚有未處理項目'}
          </Button>
        </Space>
      }>

      {loading && !impact ? <div style={{ textAlign: 'center', padding: 40 }}><Spin /></div> : null}

      {impact && (
        <Space direction="vertical" size={14} style={{ width: '100%' }}>
          {clean ? (
            <Alert type="success" showIcon
              message="沒有任何簽核設定掛在這個人身上，可以停用"
              description="按下「確定停用」就會生效。" />
          ) : (
            <Alert type="warning" showIcon
              message={`還有 ${impact.blockingCount} 項要處理`}
              description={
                <div style={{ lineHeight: 1.9 }}>
                  ⚠ 停用之後，掛在他身上的單<b>不會自己找到別人簽</b> ——
                  畫面上也看不出異常，只是躺在那裡沒人處理。
                  <br />
                  ⭐ <b>下面第二、四類要特別注意</b>：那不是「現在卡住的單」，
                  而是<b>以後的新單子還會再送給他</b>。
                </div>
              } />
          )}

          {/* ── 一、在途待簽（這裡可以直接改派）── */}
          <div>
            <Typography.Text strong>
              一、還等他簽的單　<Tag color={impact.pendingSteps.length ? 'red' : 'default'}>
                {impact.pendingSteps.length} 關
              </Tag>
            </Typography.Text>
            {impact.pendingSteps.length === 0
              ? <span style={{ color: 'var(--c-text-muted)', marginLeft: 8 }}>沒有</span>
              : (
                <>
                  <Table<PendingStep>
                    size="small" rowKey={r => `${r.docType}-${r.stepId}`} pagination={false}
                    style={{ marginTop: 8 }}
                    dataSource={impact.pendingSteps}
                    columns={[
                      { title: '種類', dataIndex: 'docType', width: 90,
                        render: v => <Tag>{DOC_LABEL[v] ?? v}</Tag> },
                      { title: '單據', dataIndex: 'docLabel' },
                      { title: '申請人', dataIndex: 'applicantName', width: 100 },
                      { title: '關卡', width: 90,
                        render: (_, r) => `第 ${r.stepOrder} 關` },
                    ]} />
                  <Space wrap style={{ marginTop: 10 }}>
                    <span>全部改派給</span>
                    <Select
                      style={{ width: 220 }} placeholder="選擇接手的人"
                      value={toUserId} onChange={setToUserId}
                      showSearch optionFilterProp="label"
                      options={others.map(c => ({
                        value: c.id,
                        label: c.title ? `${c.name}（${c.title}）` : c.name,
                      }))} />
                    <Input
                      style={{ width: 240 }} value={reason}
                      onChange={e => setReason(e.target.value)}
                      placeholder="改派原因（必填）" />
                    <Button type="primary" disabled={!toUserId} loading={busy} onClick={reassignAll}>
                      改派
                    </Button>
                  </Space>
                  <div style={{ color: 'var(--c-text-muted)', fontSize: 'var(--fs-sm)', marginTop: 6 }}>
                    ⚠ 只會動<b>還沒簽</b>的關卡；已經簽過的不會被改。改派會留下紀錄。
                  </div>
                </>
              )}
          </div>

          {/* ── 二、組織編制上的主管身分（最容易被忽略的一類）── */}
          <div>
            <Typography.Text strong>
              二、他還是這些單位的主管
              <Tag color={impact.unitManagerOf.some(m => m.source !== VIEWER) ? 'red' : 'default'}>
                {impact.unitManagerOf.length} 筆
              </Tag>
            </Typography.Text>
            {impact.unitManagerOf.length === 0
              ? <span style={{ color: 'var(--c-text-muted)', marginLeft: 8 }}>沒有</span>
              : (
                <>
                  <div style={{ marginTop: 8 }}>
                    {impact.unitManagerOf.map((m, i) => (
                      <Tag key={i} color={m.source === VIEWER ? 'default' : 'orange'}
                           style={{ marginBottom: 4 }}>
                        {m.unitName}　{m.source}
                        {m.source === VIEWER && '（不擋停用）'}
                      </Tag>
                    ))}
                  </div>
                  <div style={{ color: 'var(--c-text-muted)', fontSize: 'var(--fs-sm)', marginTop: 6 }}>
                    ⚠⚠ <b>這一類不處理的話，下一張新單子還是會送給他。</b>
                    請到 <Link to="/org-units">組織編制</Link> 換掉這些單位的主管。
                  </div>
                </>
              )}
          </div>

          {/* ── 三、代理設定 ── */}
          <div>
            <Typography.Text strong>
              三、代理設定　<Tag color={impact.delegations.length ? 'red' : 'default'}>
                {impact.delegations.length} 筆
              </Tag>
            </Typography.Text>
            {impact.delegations.length === 0
              ? <span style={{ color: 'var(--c-text-muted)', marginLeft: 8 }}>沒有</span>
              : (
                <>
                  <div style={{ marginTop: 8 }}>
                    {impact.delegations.map(d => (
                      <Tag key={d.id} color="orange" style={{ marginBottom: 4 }}>
                        {d.side}：{d.counterpartName}（{d.startDate} ~ {d.endDate}）
                      </Tag>
                    ))}
                  </div>
                  <div style={{ color: 'var(--c-text-muted)', fontSize: 'var(--fs-sm)', marginTop: 6 }}>
                    請到 <Link to="/delegations">代理設定</Link> 停用這幾筆。
                  </div>
                </>
              )}
          </div>

          {/* ── 四、流程裡把他寫死成簽核人 ── */}
          <div>
            <Typography.Text strong>
              四、簽核流程指名他　<Tag color={impact.specificSteps.length ? 'red' : 'default'}>
                {impact.specificSteps.length} 關
              </Tag>
            </Typography.Text>
            {impact.specificSteps.length === 0
              ? <span style={{ color: 'var(--c-text-muted)', marginLeft: 8 }}>沒有</span>
              : (
                <>
                  <div style={{ marginTop: 8 }}>
                    {impact.specificSteps.map(s => (
                      <Tag key={s.stepId} color="orange" style={{ marginBottom: 4 }}>
                        {s.formName}　第 {s.stepOrder} 關
                      </Tag>
                    ))}
                  </div>
                  <div style={{ color: 'var(--c-text-muted)', fontSize: 'var(--fs-sm)', marginTop: 6 }}>
                    ⚠⚠ 這一類<b>每一張新單都會固定送給他</b>。
                    <b>目前簽核流程還沒有維護畫面</b>，要改請告知我方。
                  </div>
                </>
              )}
          </div>
        </Space>
      )}
    </Modal>
  )
}
