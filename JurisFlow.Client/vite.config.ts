import path from 'path';
import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
    const env = loadEnv(mode, '.', '');
    return {
      server: {
        port: 3000,
        host: '0.0.0.0',
        proxy: {
          '/api': {
            target: 'http://localhost:5073',
            changeOrigin: true,
            secure: false,
          },
          '/uploads': {
            target: 'http://localhost:5073',
            changeOrigin: true,
            secure: false,
          },
        }
      },
      plugins: [react()],
      build: {
        chunkSizeWarningLimit: 800,
        rollupOptions: {
          output: {
            manualChunks: {
              'vendor-react': ['react', 'react-dom'],
            }
          }
        }
      },
      define: {
        'process.env.API_KEY': JSON.stringify(env.GEMINI_API_KEY),
        'process.env.GEMINI_API_KEY': JSON.stringify(env.GEMINI_API_KEY)
      },
      resolve: {
        preserveSymlinks: true,
        alias: {
          '@': path.resolve(__dirname, '.'),
        }
      }
    };
});
