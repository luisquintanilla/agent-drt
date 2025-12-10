import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  
  // Get the gateway URL from environment variables
  const gatewayHttps = env.services__gateway__https__0
  const gatewayHttp = env.services__gateway__http__0
  const gatewayUrl = gatewayHttps || gatewayHttp || 'http://localhost:5000'
  
  return {
    plugins: [react()],
    resolve: {
      alias: {
        '@': path.resolve(__dirname, './src'),
      },
    },
    server: {
      port: parseInt(env.PORT || '5173'),
      proxy: {
        '/v1': {
          target: gatewayUrl,
          changeOrigin: true,
          secure: false,
        },
      },
    },
  }
})
