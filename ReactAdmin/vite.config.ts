import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 後台掛在 /admin/ 路徑之下（nginx 以 /admin/ 前綴分流至本站靜態檔）
export default defineConfig({
  plugins: [react()],
  base: '/admin/',
  server: {
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
})
