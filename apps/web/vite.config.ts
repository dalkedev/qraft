import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': 'http://127.0.0.1:5280',
      '/simulator-api': {
        target: 'http://127.0.0.1:5281',
        rewrite: (path) => path.replace(/^\/simulator-api/, '/api'),
      },
      '/qraft-health': {
        target: 'http://127.0.0.1:5280',
        rewrite: () => '/health/ready',
      },
      '/simulator-health': {
        target: 'http://127.0.0.1:5281',
        rewrite: () => '/health/ready',
      },
    },
  },
})
