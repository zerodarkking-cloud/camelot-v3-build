import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  base: './', // 🔥 КРИТИЧЕСКИ ВАЖНО: Делает пути относительными для Electron
  server: {
    port: 5173,
    host: '127.0.0.1' // Эта строчка связывает Vite и Electron в dev-режиме
  },
  build: {
    outDir: 'dist', // Гарантирует, что собранные файлы попадут в нужную папку
    emptyOutDir: true // Очищает папку dist перед новой сборкой, чтобы не было мусора
  }
})