import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue2';
import { readFileSync } from 'fs';
import { join } from 'path';
import { fileURLToPath, URL } from 'node:url';

const baseFolder = process.env.APPDATA !== undefined && process.env.APPDATA !== '' ? `${process.env.APPDATA}/ASP.NET/https` : `${process.env.HOME}/.aspnet/https`;

const certificateName = process.env.npm_package_name;

const certFilePath = join(baseFolder, `${certificateName}.pem`);
const keyFilePath = join(baseFolder, `${certificateName}.key`);

const target = process.env.ASPNETCORE_HTTPS_PORT
  ? `https://localhost:${process.env.ASPNETCORE_HTTPS_PORT}`
  : process.env.ASPNETCORE_URLS
  ? process.env.ASPNETCORE_URLS.split(';')[0]
  : 'http://localhost:44391';

export default defineConfig({
  plugins: [vue()],
  server: {
    https: {
      key: readFileSync(keyFilePath),
      cert: readFileSync(certFilePath)
    },
    port: 44477,
    strictPort: true,
    proxy: {
      '/api': {
        target: target,
        changeOrigin: true,
        secure: false
      }
    }
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  css: {
    devSourcemap: true
  }
})