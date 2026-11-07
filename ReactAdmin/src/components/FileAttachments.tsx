import { useCallback, useEffect, useRef, useState } from 'react'
import { Button, Upload, Space, Empty, Image, Tooltip, Popconfirm, message, Spin } from 'antd'
import { UploadOutlined, DeleteOutlined, DownloadOutlined, FileOutlined } from '@ant-design/icons'
import { api, auth, ApiError } from '../api'

export interface StoredFileRow {
  id: number
  originalName: string
  contentType: string
  sizeBytes: number
  category: string
  note: string | null
  createdAt: string
  isImage: boolean
}

interface Props {
  /** 檔案分類。決定存到哪個前綴，以及要有什麼權限才碰得到 */
  // ⚠ 要與後端 Models/Storage.cs 的 FileCategory enum 對得上（新值一律加在最後）
  category: 'Resident' | 'Crisis' | 'Record' | 'Hr' | 'Attendance' | 'Meal' | 'Asset' | 'Training' | 'Facility' | 'Governance' | 'Appraisal' | 'Donation' | 'Supervision' | 'NetworkContact' | 'Activity' | 'Board' | 'Filing' | 'Inspection' | 'StaffSupport' | 'Seal' | 'Equality' | 'Privacy' | 'InKind' | 'Nutrition'
  /** 掛在哪一種單據上 */
  ownerType: string
  ownerId: number
  /** 沒有寫入權限時只顯示、不給上傳與刪除 */
  writePermission: string
  /**
   * 強制唯讀（單據已鎖定／已送簽）。
   * ⚠ 必須是獨立的參數，不能靠傳一個不存在的權限碼來擋 ——
   * `auth.hasPerm` 對 `system.admin` 一律回 true，那樣管理員照樣看得到上傳鈕。
   */
  readOnly?: boolean
  /**
   * 可以上傳，但**不給刪**。
   * ⚠ 用途：送簽後仍要允許補件，但**已經給簽核者看過的東西不可以被抽掉**。
   *   readOnly 是連上傳一起關掉，擋不出這種「只進不出」的狀態。
   * ⚠ 這只是把按鈕藏起來；真正的把關在後端 OwnerLockedAsync(forDelete: true)。
   * 選填參數，預設可刪，其他使用處不受影響。
   */
  allowDelete?: boolean
  /** 版面：'gallery' 以縮圖排列（照片用）、'list' 逐列（文件用） */
  layout?: 'gallery' | 'list'
  /** 上限提示用（MB）；實際限制在後端，這裡只是先告訴使用者 */
  maxSizeMb?: number
  accept?: string
  emptyText?: string
  /**
   * 附件數量變動時通知外面（載入完、上傳後、刪除後都會叫）。
   * ⚠ 用途：外層狀態要看有沒有附件時 —— 沒有回呼的話，傳了檔案
   *   上面的狀態列還寫「已上傳 0 份」，會讓人以為沒傳成功。選填參數。
   */
  onCountChange?: (count: number) => void
}

const humanSize = (n: number) =>
  n >= 1024 * 1024 ? `${(n / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(n / 1024))} KB`

/**
 * 依 `accept` 算出「支援哪些格式」的提示文字。
 *
 * ⚠⚠ 提示寫死成「支援 JPG／PNG」的話，accept 明明收 PDF，使用者也會以為不能傳。
 * ⇒ 提示一律由 accept 推出來，不要再各寫各的；accept 改了文字就跟著改。
 */
const acceptHint = (accept: string | undefined, layout: 'gallery' | 'list') => {
  if (!accept) return layout === 'gallery' ? '照片' : '照片、PDF、Word／Excel'
  const a = accept.toLowerCase()
  const parts: string[] = []
  if (a.includes('image/')) parts.push('照片（JPG／PNG／HEIC／WEBP）')
  if (a.includes('pdf')) parts.push('PDF')
  if (a.includes('word') || a.includes('.doc')) parts.push('Word')
  if (a.includes('sheet') || a.includes('.xls') || a.includes('csv')) parts.push('Excel')
  return parts.length > 0 ? parts.join('、') : accept
}

/**
 * 單據附件。上傳、預覽、下載、移除。
 *
 * ⚠ 圖片預覽必須先用 fetch 帶著權杖取回 blob，不能直接寫
 * `<img src="/api/files/1">` —— 那樣瀏覽器不會帶 Authorization 標頭。
 * 這是「下載走 API 代理、不發簽章網址」的必然代價，
 * 換來的是每一次調閱都留得下紀錄。
 */
export default function FileAttachments({
  category, ownerType, ownerId, writePermission, readOnly = false, allowDelete = true,
  layout = 'list', maxSizeMb = 20, accept, emptyText, onCountChange,
}: Props) {
  const canWrite = !readOnly && auth.hasPerm(writePermission)
  const canRemove = canWrite && allowDelete
  const [rows, setRows] = useState<StoredFileRow[]>([])
  const [loading, setLoading] = useState(false)
  const [uploading, setUploading] = useState(false)
  /** id → object URL；⚠ 卸載時要 revoke，否則整頁的圖片會留在記憶體裡 */
  const [urls, setUrls] = useState<Record<number, string>>({})
  const urlsRef = useRef<Record<number, string>>({})

  /* ⚠ 用 ref 存回呼：直接放進 load 的相依陣列，父層每次 render 產生新函式就會
   *   讓 load 換身分 → useEffect 重跑 → 無限重新載入。 */
  const cbRef = useRef(onCountChange)
  cbRef.current = onCountChange

  const load = useCallback(async () => {
    if (!ownerId) { setRows([]); cbRef.current?.(0); return }
    setLoading(true)
    try {
      const list = await api.get<StoredFileRow[]>(
        `/api/files?ownerType=${encodeURIComponent(ownerType)}&ownerId=${ownerId}`)
      setRows(list)
      cbRef.current?.(list.length)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '附件載入失敗')
    } finally { setLoading(false) }
  }, [ownerType, ownerId])

  useEffect(() => { load() }, [load])

  // 圖片縮圖：逐張取回 blob 再轉成本地網址
  useEffect(() => {
    let cancelled = false
    const wanted = rows.filter(r => r.isImage && !urlsRef.current[r.id])
    if (wanted.length === 0) return
    ;(async () => {
      for (const r of wanted) {
        try {
          const blob = await api.blob(`/api/files/${r.id}`)
          if (cancelled) return
          const u = URL.createObjectURL(blob)
          urlsRef.current[r.id] = u
          setUrls({ ...urlsRef.current })
        } catch { /* 單張失敗就顯示成一般檔案，不擋整頁 */ }
      }
    })()
    return () => { cancelled = true }
  }, [rows])

  // 卸載時釋放所有 object URL
  useEffect(() => () => {
    Object.values(urlsRef.current).forEach(URL.revokeObjectURL)
    urlsRef.current = {}
  }, [])

  const doUpload = async (file: File) => {
    if (file.size > maxSizeMb * 1024 * 1024) {
      message.error(`檔案 ${humanSize(file.size)} 超過上限 ${maxSizeMb} MB`)
      return false
    }
    setUploading(true)
    try {
      const fd = new FormData()
      fd.append('file', file)
      fd.append('category', category)
      fd.append('ownerType', ownerType)
      fd.append('ownerId', String(ownerId))
      const res = await api.upload<{ duplicated?: boolean }>('/api/files', fd)
      message.success(res.duplicated ? '這份檔案已經上傳過了' : '已上傳')
      await load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '上傳失敗')
    } finally { setUploading(false) }
    return false   // 一律回 false：由我們自己送，不走 antd 內建上傳
  }

  const download = async (r: StoredFileRow) => {
    try {
      const blob = await api.blob(`/api/files/${r.id}`)
      const u = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = u
      a.download = r.originalName
      a.click()
      URL.revokeObjectURL(u)
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '下載失敗')
    }
  }

  const remove = async (r: StoredFileRow) => {
    try {
      await api.del(`/api/files/${r.id}`)
      if (urlsRef.current[r.id]) {
        URL.revokeObjectURL(urlsRef.current[r.id])
        delete urlsRef.current[r.id]
      }
      message.success('已移除')
      await load()
    } catch (e) {
      message.error(e instanceof ApiError ? e.message : '移除失敗')
    }
  }

  if (!ownerId) {
    return <div style={{ fontSize: 'var(--fs-sm)', color: 'var(--c-text-muted)' }}>請先儲存此筆資料，之後才能上傳附件</div>
  }

  const uploader = canWrite && (
    <Upload beforeUpload={doUpload} showUploadList={false} accept={accept} multiple>
      <Button size="small" icon={<UploadOutlined />} loading={uploading}>
        上傳{layout === 'gallery' && !/pdf|doc|xls|sheet|csv/i.test(accept ?? '') ? '照片' : '檔案'}
      </Button>
    </Upload>
  )

  return (
    <Spin spinning={loading}>
      <Space direction="vertical" size={8} style={{ width: '100%' }}>
        {/* ⚠ 不能上傳時就不要顯示上限提示 —— 對唯讀的人說「單檔上限 20 MB」只是雜訊 */}
        {canWrite && (
          <Space wrap>
            {uploader}
            <span style={{ fontSize: 'var(--fs-xs)', color: 'var(--c-text-muted)' }}>
              單檔上限 {maxSizeMb} MB；支援 {acceptHint(accept, layout)}
            </span>
          </Space>
        )}

        {rows.length === 0 ? (
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE}
            description={<span style={{ fontSize: 'var(--fs-sm)' }}>{emptyText ?? '尚無附件'}</span>} />
        ) : layout === 'gallery' ? (
          <Space wrap size={8}>
            {rows.map(r => (
              <div key={r.id} style={{ width: 104, textAlign: 'center' }}>
                <div style={{
                  width: 104, height: 104, borderRadius: 6, overflow: 'hidden',
                  border: '1px solid var(--c-border)', display: 'flex',
                  alignItems: 'center', justifyContent: 'center', background: 'var(--c-bg-subtle)',
                }}>
                  {r.isImage && urls[r.id]
                    ? <Image src={urls[r.id]} width={104} height={104}
                        style={{ objectFit: 'cover' }} alt={r.originalName} />
                    : r.isImage
                      ? <Spin size="small" />
                      : <FileOutlined style={{ fontSize: 28, color: 'var(--c-text-muted)' }} />}
                </div>
                <Tooltip title={`${r.originalName}（${humanSize(r.sizeBytes)}）`}>
                  <div style={{
                    fontSize: 'var(--fs-xs)', marginTop: 2, whiteSpace: 'nowrap',
                    overflow: 'hidden', textOverflow: 'ellipsis',
                  }}>{r.originalName}</div>
                </Tooltip>
                <Space size={2}>
                  <Button type="link" size="small" icon={<DownloadOutlined />}
                    onClick={() => download(r)} />
                  {canRemove && (
                    <Popconfirm title={r.isImage ? '移除這張照片？' : '移除這個檔案？'} onConfirm={() => remove(r)}>
                      <Button type="link" size="small" danger icon={<DeleteOutlined />} />
                    </Popconfirm>
                  )}
                </Space>
              </div>
            ))}
          </Space>
        ) : (
          <div>
            {rows.map(r => (
              <div key={r.id} style={{
                display: 'flex', alignItems: 'center', gap: 8,
                padding: '4px 0', borderBottom: '1px solid var(--c-bg-page)', fontSize: 'var(--fs-sm)',
              }}>
                {r.isImage && urls[r.id]
                  ? <Image src={urls[r.id]} width={32} height={32}
                      style={{ objectFit: 'cover', borderRadius: 4 }} alt={r.originalName} />
                  : <FileOutlined style={{ color: 'var(--c-text-muted)' }} />}
                <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {r.originalName}
                </span>
                <span style={{ color: 'var(--c-text-muted)' }}>{humanSize(r.sizeBytes)}</span>
                <Button type="link" size="small" icon={<DownloadOutlined />}
                  onClick={() => download(r)} />
                {canRemove && (
                  <Popconfirm title="移除這個檔案？" onConfirm={() => remove(r)}>
                    <Button type="link" size="small" danger icon={<DeleteOutlined />} />
                  </Popconfirm>
                )}
              </div>
            ))}
          </div>
        )}
      </Space>
    </Spin>
  )
}
