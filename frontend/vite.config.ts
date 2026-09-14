import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // During development, forward API requests to our local C# backend.
    proxy: {
      '/api': process.env.PHARMACY_API_URL ?? 'http://localhost:5167',
    },
  },
})
