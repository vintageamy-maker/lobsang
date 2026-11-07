/*
 * 用手機（或電腦）相機掃條碼。
 *
 * ⚠ 同一份檔案在 React/src 和 ReactAdmin/src/components 各放一份（兩個前端是分開建置的），改一邊要同步另一邊。
 * ⭐ 用 barcode-detector 的 ponyfill（底層是 zxing-wasm）：iPhone Safari 沒有內建 BarcodeDetector，
 *    用 ponyfill 才能 iPhone、Android、電腦都一樣能掃。
 * ⚠ wasm 檔從我們自己的網站載入（Vite 打包進 assets），不走套件預設的 jsDelivr CDN：
 *    CDN 連不到或被擋時掃描會整個沒反應，而且看不出原因。
 * ⚠ 相機只能在 https（或 localhost）使用；http 開的網頁 navigator.mediaDevices 是 undefined。
 */
import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { BarcodeDetector, prepareZXingModule } from 'barcode-detector/ponyfill'
import wasmUrl from 'zxing-wasm/reader/zxing_reader.wasm?url'

prepareZXingModule({
  overrides: {
    locateFile: (path: string, prefix: string) => (path.endsWith('.wasm') ? wasmUrl : prefix + path),
  },
})

/** 這個瀏覽器能不能開相機（http 網頁、很舊的瀏覽器會是 false） */
export function canScan(): boolean {
  return typeof navigator !== 'undefined' && !!navigator.mediaDevices?.getUserMedia
}

/**
 * 從一張畫面裡挑出要用的條碼。
 * ⚠ 日本書背面有兩條：上面 978 開頭的 ISBN、下面 191／192 開頭的分類定價碼。
 *    後者查不到書，所以忽略它，繼續等使用者對準 ISBN 那條。
 */
function pick(values: string[]): string | null {
  const ok = values.filter(v => /^\d{8,13}$/.test(v) && !/^19[12]/.test(v))
  return ok.find(v => /^97[89]/.test(v)) ?? ok[0] ?? null
}

function errorText(e: unknown): string {
  const name = e instanceof DOMException ? e.name : ''
  if (name === 'NotAllowedError' || name === 'SecurityError')
    return '沒有相機權限。請在瀏覽器的網站設定裡允許這個網站使用相機，再按一次掃描。'
  if (name === 'NotFoundError' || name === 'OverconstrainedError') return '找不到可以用的相機。'
  if (name === 'NotReadableError') return '相機被其他程式佔用中，請先關掉其他使用相機的 App。'
  return '相機開不起來，請改用手動輸入。'
}

/** 連續模式下，同一個條碼要隔多久才算「又掃一次」（條碼停在鏡頭前時不會一直重複送出） */
const SAME_CODE_GAP_MS = 3000

/**
 * continuous：連續模式。掃到後相機不關，直接等下一個（後台一次掃很多本用）。
 * children：顯示在相機畫面下方（連續模式用來顯示掃了幾本、結果如何）。
 */
export function BarcodeScanner({ onDetect, onClose, continuous = false, children }: {
  onDetect: (code: string) => void
  onClose: () => void
  continuous?: boolean
  children?: ReactNode
}) {
  const videoRef = useRef<HTMLVideoElement>(null)
  const [err, setErr] = useState<string | null>(null)
  const [ready, setReady] = useState(false)
  const [flash, setFlash] = useState(false)
  const continuousRef = useRef(continuous)
  continuousRef.current = continuous
  // 回呼放 ref：父元件重新渲染換了函式，也不要重開相機
  const detectRef = useRef(onDetect)
  detectRef.current = onDetect

  useEffect(() => {
    let stopped = false
    let stream: MediaStream | null = null
    let timer: number | undefined
    let flashTimer: number | undefined
    let last = { code: '', at: 0 }

    ;(async () => {
      try {
        const detector = new BarcodeDetector({ formats: ['ean_13', 'ean_8', 'upc_a', 'upc_e'] })
        stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 }, height: { ideal: 720 } },
          audio: false,
        })
        if (stopped) return
        const video = videoRef.current!
        video.srcObject = stream
        await video.play()
        setReady(true)

        const tick = async () => {
          if (stopped) return
          try {
            if (video.readyState >= 2) {
              const found = pick((await detector.detect(video)).map(b => b.rawValue))
              if (found && !stopped) {
                if (!continuousRef.current) {
                  stopped = true
                  navigator.vibrate?.(80)
                  detectRef.current(found)
                  return
                }
                const now = Date.now()
                if (found !== last.code || now - last.at > SAME_CODE_GAP_MS) {
                  navigator.vibrate?.(80)
                  setFlash(true)
                  window.clearTimeout(flashTimer)
                  flashTimer = window.setTimeout(() => setFlash(false), 500)
                  detectRef.current(found)
                }
                last = { code: found, at: now }
              }
            }
          } catch {
            /* 單張辨識失敗就換下一張 */
          }
          timer = window.setTimeout(tick, 150)
        }
        tick()
      } catch (e) {
        if (!stopped) setErr(errorText(e))
      }
    })()

    return () => {
      stopped = true
      window.clearTimeout(timer)
      window.clearTimeout(flashTimer)
      stream?.getTracks().forEach(t => t.stop())
    }
  }, [])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div role="dialog" aria-modal="true" aria-label="掃描條碼" style={S.backdrop}>
      <video ref={videoRef} playsInline muted style={S.video} />
      {!err && <div style={flash ? { ...S.frame, border: '5px solid #22c55e' } : S.frame} />}
      <div style={S.top}>
        {err ?? (!ready ? '正在開啟相機…'
          : continuous ? '連續掃描：框變綠就是掃到了，直接換下一本' : '把條碼放進框內，對準後會自動查詢')}
        {!err && ready && <div style={S.sub}>日本書請掃上面 978 開頭那條</div>}
      </div>
      <div style={S.bottom}>
        {children}
        <button type="button" onClick={onClose} style={S.close}>{continuous ? '完成' : '關閉'}</button>
      </div>
    </div>
  )
}

const S: Record<string, CSSProperties> = {
  backdrop: { position: 'fixed', inset: 0, zIndex: 2000, background: '#000', display: 'flex', alignItems: 'center', justifyContent: 'center' },
  video: { width: '100%', height: '100%', objectFit: 'cover' },
  frame: {
    position: 'absolute', width: 'min(80vw, 420px)', height: 'min(36vw, 190px)',
    border: '3px solid #f59e0b', borderRadius: 12, boxShadow: '0 0 0 100vmax rgba(0,0,0,.45)',
  },
  top: {
    position: 'absolute', top: 'max(24px, env(safe-area-inset-top))', left: 16, right: 16,
    color: '#fff', textAlign: 'center', fontSize: 16, lineHeight: 1.5, textShadow: '0 1px 3px rgba(0,0,0,.8)',
  },
  sub: { fontSize: 13, opacity: 0.85 },
  bottom: {
    position: 'absolute', bottom: 'max(28px, env(safe-area-inset-bottom))', left: 16, right: 16,
    display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12,
  },
  close: {
    padding: '12px 36px', fontSize: 16, fontWeight: 700, color: '#0f172a', background: '#fff',
    border: 'none', borderRadius: 999, cursor: 'pointer',
  },
}
