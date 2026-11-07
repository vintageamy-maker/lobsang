import { useEffect, useState } from 'react'
import { Modal, Form, Input, Alert, message, Typography } from 'antd'
import { api, ApiError, auth } from '../api'

/**
 * 修改自己的登入密碼（2026-08-22 新增）。
 *
 * ── 為什麼到現在才有 ───────────────────────────────────────────
 * ⚠⚠ 後端的 `POST /api/auth/change-password` **一直都在**，
 *    但前端從來沒有任何地方呼叫它 —— 也就是說在此之前，
 *    **同仁想換自己的密碼只能請系統管理員代為重設**。
 *    那代表這組密碼至少有兩個人知道，違背密碼的基本意義。
 *
 * ⚠⚠ 更嚴重的是 `MustChangePassword`：後端會在建立帳號時把它設為 true，
 *    前端卻既不讀、也沒有換密碼的畫面 ——
 *    那個機制不但攔不住人，**被設到的人還沒有出路**。
 *    ⇒ 這個元件同時解決兩件事：自助修改、以及強制換密碼的出口。
 *
 * ── ⚠ 與「認證密碼」是兩回事 ──────────────────────────────────
 * 系統另有 `/my/step-up-password`（認證密碼／二次密碼），
 * 那道密碼保護的是**敏感資料**，與登入密碼**不可共用**。
 * 兩頁的文案都要講清楚，不然使用者會以為改了一個就等於改了兩個。
 */

interface Props {
  open: boolean
  /** 強制模式：帳號被設成「下次登入須改密碼」。⚠ 此時不可關閉，也不給取消 */
  forced?: boolean
  onClose: () => void
}

export default function ChangePasswordModal({ open, forced = false, onClose }: Props) {
  const [form] = Form.useForm()
  const [busy, setBusy] = useState(false)

  // ⚠ 每次開啟都清空 —— 密碼欄殘留上一次輸入的內容是安全問題，不只是體驗問題
  useEffect(() => { if (open) form.resetFields() }, [open, form])

  const submit = async () => {
    const v = await form.validateFields()
    setBusy(true)
    try {
      await api.post('/api/auth/change-password', {
        oldPassword: v.oldPassword, newPassword: v.newPassword,
      })
      /*
       * ⭐ 換成功後要把本機那份使用者資料的旗標清掉 ——
       *   後端已經把 MustChangePassword 設成 false，
       *   但前端存的是登入當下的快照，不清的話下次進來還會被擋。
       */
      const u = auth.user()
      if (u?.mustChangePassword) auth.save(auth.token()!, { ...u, mustChangePassword: false })
      message.success('密碼已修改')
      onClose()
    } catch (e) {
      // ⚠ 後端把「舊密碼錯」與「新密碼太短」分成不同 code，訊息直接用它的 ——
      //   自己改寫成通用句子，使用者就不知道是哪一個欄位有問題
      message.error(e instanceof ApiError ? e.message : '修改失敗')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      title={forced ? '請先修改密碼' : '修改登入密碼'}
      onOk={submit}
      onCancel={forced ? undefined : onClose}
      confirmLoading={busy}
      okText="修改"
      cancelText="取消"
      /* ⚠ 強制模式下不能有退路：關閉鈕、遮罩點擊、Esc 全部擋掉，
       *   否則使用者關掉視窗就繞過了，強制就不是強制。 */
      closable={!forced}
      maskClosable={!forced}
      keyboard={!forced}
      cancelButtonProps={forced ? { style: { display: 'none' } } : undefined}
      destroyOnClose
    >
      {forced && (
        <Alert
          type="warning" showIcon style={{ marginBottom: 14 }}
          message="這個帳號目前使用的是預設密碼"
          description="為了帳號安全，請改成只有你自己知道的密碼之後再繼續使用。"
        />
      )}

      <Form form={form} layout="vertical" onFinish={submit}>
        <Form.Item
          name="oldPassword" label="目前的密碼"
          rules={[{ required: true, message: '請輸入目前的密碼' }]}
        >
          <Input.Password autoComplete="current-password" autoFocus />
        </Form.Item>

        <Form.Item
          name="newPassword" label="新密碼"
          /* ⚠ 長度要與後端一致（後端擋 8 碼）。前端寬、後端嚴的話，
           *   使用者會填完才被退，而且看不出是哪裡不合 */
          rules={[
            { required: true, message: '請輸入新密碼' },
            { min: 8, message: '新密碼至少 8 碼' },
          ]}
          extra="至少 8 碼"
        >
          <Input.Password autoComplete="new-password" />
        </Form.Item>

        <Form.Item
          name="confirm" label="再輸入一次新密碼"
          dependencies={['newPassword']}
          rules={[
            { required: true, message: '請再輸入一次' },
            /* ⚠ 兩次不一致要在**送出前**擋下來 —— 後端只收一個新密碼，
             *   打錯了會直接生效，使用者下次就登不進來了 */
            ({ getFieldValue }) => ({
              validator: (_, value) =>
                !value || getFieldValue('newPassword') === value
                  ? Promise.resolve()
                  : Promise.reject(new Error('兩次輸入的新密碼不一樣')),
            }),
          ]}
        >
          <Input.Password autoComplete="new-password" />
        </Form.Item>
      </Form>

      <Typography.Text type="secondary" style={{ fontSize: 12, lineHeight: 1.8 }}>
        ⚠ 這是<b>登入系統</b>用的密碼。
        <br />
        進入敏感資料頁面時要再輸入的那一道叫<b>認證密碼</b>，
        在「我的認證密碼」那一頁改，<b>兩者不共用</b>。
      </Typography.Text>
    </Modal>
  )
}
