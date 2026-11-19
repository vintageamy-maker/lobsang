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

/**
 * 補「白色留白」再讀一次用（2026-11-19 業主：深色封面的書掃不到，例：ISBN 9780735846289 The Night）。
 * ⚠ 有些書把條碼直接印在深色封面上，黑線兩旁只留一根細線寬的白邊，外面就是深色底，
 *    沒有條碼規定的「留白區」，zxing 找不到條碼的頭尾就讀不出來（tryInvert 也沒用，那是白線深底另一種情況）。
 * ⭐ 做法：一列一列看，比這列平均暗的連續一段如果長過「一般線條寬度的 5 倍」，就不可能是條碼的線，
 *    把它塗白，等於幫條碼補上留白，再交給 zxing 讀。
 *    binary=false 保留原本灰階（解析度低、線條細時比較準）；true 改成純黑白（解析度高時比較準），
 *    兩種輪流試（用業主的照片在 5 種解析度下測過，每種至少一個讀得到）。
 * ⭐ 只有一般讀法讀不到時才做，平常掃得到的條碼不受影響；EAN／UPC 有檢查碼，不會讀出錯的號碼。
 */
function fillQuietZones(src: Uint8ClampedArray, w: number, h: number, binary: boolean): ImageData {
  const out = new ImageData(w, h)
  const o = out.data
  const gray = new Float32Array(w)
  const runs: number[] = []
  const lens: number[] = []
  for (let y = 0; y < h; y++) {
    let sum = 0
    for (let x = 0; x < w; x++) {
      const i = (y * w + x) * 4
      const g = src[i] * 0.299 + src[i + 1] * 0.587 + src[i + 2] * 0.114
      gray[x] = g
      sum += g
    }
    const thr = sum / w
    runs.length = 0
    let start = 0
    for (let x = 1; x <= w; x++) {
      if (x === w || (gray[x] < thr) !== (gray[start] < thr)) { runs.push(start, x - start); start = x }
    }
    lens.length = 0
    for (let k = 1; k < runs.length; k += 2) lens.push(runs[k])
    lens.sort((a, b) => a - b)
    const limit = Math.max(8, lens[lens.length >> 1] * 5)
    for (let k = 0; k < runs.length; k += 2) {
      const s = runs[k], n = runs[k + 1]
      const dark = gray[s] < thr
      const fill = dark && n > limit
      for (let x = s; x < s + n; x++) {
        const i = (y * w + x) * 4
        const v = fill ? 255 : binary ? (dark ? 0 : 255) : gray[x]
        o[i] = o[i + 1] = o[i + 2] = v
        o[i + 3] = 255
      }
    }
  }
  return out
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

        // 一般讀法讀不到時，把畫面畫到 canvas、補留白再讀一次（見 fillQuietZones）
        const canvas = document.createElement('canvas')
        const ctx = canvas.getContext('2d', { willReadFrequently: true })
        let retry = 0
        const detectFilled = async (): Promise<string | null> => {
          const w = video.videoWidth, h = video.videoHeight
          if (!ctx || !w || !h) return null
          if (canvas.width !== w) canvas.width = w
          if (canvas.height !== h) canvas.height = h
          ctx.drawImage(video, 0, 0, w, h)
          const img = fillQuietZones(ctx.getImageData(0, 0, w, h).data, w, h, retry++ % 2 === 1)
          return pick((await detector.detect(img)).map(b => b.rawValue))
        }

        const tick = async () => {
          if (stopped) return
          try {
            if (video.readyState >= 2) {
              const found = pick((await detector.detect(video)).map(b => b.rawValue)) ?? (stopped ? null : await detectFilled())
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
