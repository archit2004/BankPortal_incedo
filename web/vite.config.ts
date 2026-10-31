import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The dev server forwards /api/* to the ASP.NET API, so the browser sees a single origin (no CORS needed).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5180', changeOrigin: true },
    },
  },
});