import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 開發時 /api 轉發到本機 API (5080)；正式環境由 nginx 分流，前端一律以相對路徑呼叫 /api
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
})
