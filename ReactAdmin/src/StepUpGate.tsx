import { useCallback, useEffect, useRef, useState } from 'react'
import { Modal, Form, Input, Alert, Button, Space, Typography, message } from 'antd'
import { LockOutlined, LogoutOutlined } from '@ant-design/icons'
import { Link } from 'react-router-dom'
import { api, ApiError, setStepUpToken, onStepUpRequired, onStepUpExpiry } from './api'

/**
 * 認證密碼（二次密碼）閘門。
 *
 * ⭐ 包住「看得到個人金額」的頁（業主 2026-08-14 拍板的判準）。
 *    一次認證，所有受保護的頁共用 —— 換頁不用重輸。
 *
 * ⚠ **這裡不是那道鎖。** 真正的鎖在後端：受保護的端點沒有解鎖憑證就回 403。
 *    本元件只做兩件事：把跳窗叫出來、把倒數顯示出來。
 *    倒數的時間一律以**後端回的到期時間**為準，不是前端自己算的。
 */

const { Text } = Typography

interface Status {
  hasPassword: boolean
  unlocked: boolean
  expiresInSeconds: number
  idleMinutes: number
  passwordRule: string
}

/** 剩餘時間顯示成 mm:ss */
function remain(deadline: number | null): string {
  if (!deadline) return "--:--"
  const ms = deadline - Date.now()
  if (ms <= 0) return '00:00'
  const s = Math.floor(ms / 1000)
  return `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`
}

export default function StepUpGate({ children }: { children: React.ReactNode }) {
  const [status, setStatus] = useState<Status | null>(null)
  const [asking, setAsking] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  /* ⚠ 存「本機的到期時刻」，由後端給的「還剩幾秒」換算 ——
     後端回絕對時間會踩到時區（容器 UTC、瀏覽器 UTC+8，差 8 小時） */
  const [deadline, setDeadline] = useState<number | null>(null)
  const [, tick] = useState(0)
  const [form] = Form.useForm()
  const expiredRef = useRef(false)

  const load = useCallback(async () => {
    try {
      const s: Status = await api.get('/api/auth/step-up/status')
      setStatus(s)
      setDeadline(s.unlocked ? Date.now() + s.expiresInSeconds * 1000 : null)
      if (!s.unlocked) setAsking(true)
    } catch { /* 未登入等情況由外層處理 */ }
  }, [])
  useEffect(() => { void load() }, [load])

  // 後端每次回應都帶剩餘時間 —— 以它為準
  useEffect(() => onStepUpExpiry(sec => {
    setDeadline(Date.now() + sec * 1000); expiredRef.current = false
  }), [])
  useEffect(() => onStepUpRequired(() => { setDeadline(null); setAsking(true) }), [])

  // 每秒重畫倒數；歸零就叫使用者重新認證
  useEffect(() => {
    const t = setInterval(() => {
      tick(n => n + 1)
      if (deadline && deadline <= Date.now() && !expiredRef.current) {
        expiredRef.current = true
        setStepUpToken(null)
        setDeadline(null)
        setAsking(true)
        message.warning('閒置過久，請重新輸入認證密碼')
      }
    }, 1000)
    return () => clearInterval(t)
  }, [deadline])

  const unlock = async () => {
    const v = await form.validateFields()
    setBusy(true); setErr(null)
    try {
      const r = await api.post<{ token: string; expiresInSeconds: number }>(
        '/api/auth/step-up', { password: v.password })
      setStepUpToken(r.token)
      setDeadline(Date.now() + r.expiresInSeconds * 1000)
      expiredRef.current = false
      setAsking(false)
      form.resetFields()
      await load()
    } catch (e) {
      setErr(e instanceof ApiError ? e.message : '驗證失敗，請重新輸入')
    } finally { setBusy(false) }
  }

  const logout = async () => {
    try { await api.post('/api/auth/step-up/logout') } catch { /* 已失效也算登出 */ }
    setStepUpToken(null); setDeadline(null); setAsking(true)
  }

  const unlocked = !!deadline && deadline > Date.now()

  return (
    <>
      {unlocked && (
        <Alert
          type="warning" showIcon style={{ marginBottom: 12 }}
          message={
            <Space>
              <span>如您未繼續操作，此功能將於 <Text strong>{remain(deadline)}</Text> 後自動登出</span>
            </Space>
          }
          action={
            <Button size="small" icon={<LogoutOutlined />} onClick={logout}>認證密碼登出</Button>
          }
        />
      )}

      {unlocked ? children : (
        <Alert
          type="info" showIcon
          message="此功能受認證密碼保護"
          description="輸入認證密碼後方可繼續使用此功能。"
          action={<Button type="primary" icon={<LockOutlined />}
            onClick={() => setAsking(true)}>輸入認證密碼</Button>}
        />
      )}

      <Modal
        title="請輸入認證密碼"
        open={asking}
        onOk={unlock}
        onCancel={() => { setAsking(false); setErr(null) }}
        confirmLoading={busy}
        okText="確認" cancelText="返回"
        forceRender destroyOnHidden={false}
        maskClosable={false}>
        {status && !status.hasPassword ? (
          <Alert
            type="warning" showIcon
            message="尚未設定認證密碼"
            description={<>請先到<Link to="/my/step-up-password" onClick={() => setAsking(false)}><b>「我的認證密碼」</b></Link>設定後再使用此功能。<br />
              規則：{status.passwordRule}</>}
          />
        ) : (
          <>
            <p>輸入認證密碼後方可繼續使用此功能。</p>
            <p style={{ color: '#64748b' }}>{status?.passwordRule ?? ''}</p>
            <Form form={form} layout="vertical" onFinish={unlock}>
              <Form.Item
                name="password" label="認證密碼"
                rules={[{ required: true, message: '請輸入認證密碼' }]}>
                <Input.Password autoFocus autoComplete="off" />
              </Form.Item>
            </Form>
            {err && <Alert type="error" showIcon message={err} />}
            <div style={{ marginTop: 8, color: '#64748b', fontSize: 12 }}>
              忘記密碼？請洽系統管理員重設（重設會留下稽核紀錄）。
            </div>
          </>
        )}
      </Modal>
    </>
  )
}
