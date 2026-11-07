import { useCallback, useEffect, useState } from 'react'
import { Card, Form, Input, Button, Alert, message, Descriptions, Tag, Select, Space } from 'antd'
import { api, ApiError, auth } from '../api'

/**
 * 我的認證密碼（二次密碼）。
 *
 * ⚠ 強度規範**等同或嚴於登入密碼**（登入 8 碼）。
 *   參考系統的密碼政策只勾了登入密碼、認證密碼完全不受長度與複雜度限制，
 *   而那道密碼保護的正是敏感資料 —— 本案不複製這個缺口。
 */
export default function MyStepUpPassword() {
  const [status, setStatus] = useState<any>(null)
  const [busy, setBusy] = useState(false)
  const [form] = Form.useForm()
  const [resetForm] = Form.useForm()
  const [users, setUsers] = useState<any[]>([])

  const isAdmin = (auth.user()?.permissions ?? []).includes('system.admin')

  const load = useCallback(async () => {
    try { setStatus(await api.get('/api/auth/step-up/status')) }
    catch (e) { message.error(e instanceof ApiError ? e.message : '載入失敗') }
    if (isAdmin) {
      try {
        const u: any = await api.get('/api/admin/users?pageSize=500')
        setUsers((u.items ?? u).filter((x: any) => x.isActive))
      } catch { /* 沒有帳號管理權限就不顯示 */ }
    }
  }, [isAdmin])
  useEffect(() => { void load() }, [load])

  const save = async () => {
    const v = await form.validateFields()
    setBusy(true)
    try {
      await api.post('/api/auth/step-up/password', {
        currentPassword: v.current ?? null, newPassword: v.next,
      })
      message.success('已設定。⚠ 既有的解鎖狀態已一併失效，請重新認證。')
      form.resetFields()
      await load()
    } catch (e) { message.error(e instanceof ApiError ? e.message : '設定失敗') }
    finally { setBusy(false) }
  }

  const reset = async () => {
    const v = await resetForm.validateFields()
    setBusy(true)
    try {
      await api.post('/api/auth/step-up/reset', { userId: v.userId, newPassword: v.next })
      message.success('已重設。該員的解鎖狀態已一併失效，操作已留稽核紀錄。')
      resetForm.resetFields()
    } catch (e) { message.error(e instanceof ApiError ? e.message : '重設失敗') }
    finally { setBusy(false) }
  }

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <Card title="我的認證密碼" loading={!status}>
        <Alert
          type="info" showIcon style={{ marginBottom: 16 }}
          message="這是進入敏感資料頁面的第二道密碼"
          description={
            <>
              以 StepUpGate 包住的頁面需要它。
              <br />一次認證，這些頁面共用；閒置 {status?.idleMinutes ?? 30} 分鐘後自動失效。
              <br /><b>規則：{status?.passwordRule ?? '至少 8 碼，且必須同時包含英文字母與數字'}</b>
              —— 與登入密碼同等或更嚴，因為它保護的是敏感資料。
            </>
          }
        />
        {status && !status.eligible && (
          <Alert
            type="warning" showIcon style={{ marginBottom: 16 }}
            message="你的帳號不需要認證密碼"
            description="只有具備敏感資料權限的帳號需要設定 —— 其他帳號本來就看不到這些頁面。"
          />
        )}

        <Descriptions size="small" bordered column={2} style={{ marginBottom: 16 }}>
          <Descriptions.Item label="是否已設定">
            {status?.hasPassword ? <Tag color="green">已設定</Tag> : <Tag color="red">尚未設定</Tag>}
          </Descriptions.Item>
          <Descriptions.Item label="目前是否解鎖中">
            {status?.unlocked ? <Tag color="green">解鎖中</Tag> : <Tag>未解鎖</Tag>}
          </Descriptions.Item>
        </Descriptions>

        {status?.eligible !== false && (
        <Form form={form} layout="vertical" style={{ maxWidth: 420 }}>
          {status?.hasPassword && (
            <Form.Item
              name="current" label="目前的認證密碼"
              rules={[{ required: true, message: '請輸入目前的認證密碼' }]}
              extra="⚠ 變更需先驗證舊密碼，避免有人趁你離開座位時換掉">
              <Input.Password autoComplete="off" />
            </Form.Item>
          )}
          <Form.Item
            name="next" label="新的認證密碼"
            rules={[
              { required: true, message: '請輸入新的認證密碼' },
              { min: 8, message: '至少 8 碼' },
              { pattern: /(?=.*[A-Za-z])(?=.*\d)/, message: '必須同時包含英文字母與數字' },
            ]}>
            <Input.Password autoComplete="new-password" />
          </Form.Item>
          <Button type="primary" loading={busy} onClick={save}>
            {status?.hasPassword ? '變更認證密碼' : '設定認證密碼'}
          </Button>
        </Form>
        )}
      </Card>

      {isAdmin && (
        <Card title="重設他人的認證密碼（系統管理員）">
          <Alert
            type="warning" showIcon style={{ marginBottom: 16 }}
            message="這是「忘記認證密碼」的解法"
            description="重設後該員的解鎖狀態會立即失效，且此操作一定會留下稽核紀錄（誰、在什麼時候、重設了誰）。"
          />
          <Form form={resetForm} layout="vertical" style={{ maxWidth: 420 }}>
            <Form.Item name="userId" label="對象" rules={[{ required: true, message: '請選擇對象' }]}>
              <Select
                showSearch optionFilterProp="label" placeholder="選擇員工"
                options={users.map(u => ({
                  value: u.id, label: `${u.employeeNo ?? ''} ${u.name}`.trim(),
                }))}
              />
            </Form.Item>
            <Form.Item
              name="next" label="新的認證密碼"
              rules={[
                { required: true, message: '請輸入新的認證密碼' },
                { min: 8, message: '至少 8 碼' },
                { pattern: /(?=.*[A-Za-z])(?=.*\d)/, message: '必須同時包含英文字母與數字' },
              ]}>
              <Input.Password autoComplete="new-password" />
            </Form.Item>
            <Button danger loading={busy} onClick={reset}>重設</Button>
          </Form>
        </Card>
      )}
    </Space>
  )
}
