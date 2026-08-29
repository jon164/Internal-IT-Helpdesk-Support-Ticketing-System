/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],

  server: {
    port: 5173,
    // The client calls /api/... on its own origin and Vite forwards it to the backend. This keeps
    // the API base URL out of the client code and means CORS is only a development concern.
    proxy: {
      '/api': {
        target: 'http://localhost:5099',
        changeOrigin: true,
      },
    },
  },

  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})
