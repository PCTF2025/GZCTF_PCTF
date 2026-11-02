import react from '@vitejs/plugin-react'
import process from 'process'
import { defineConfig, loadEnv } from 'vite'
import banner from 'vite-plugin-banner'
import { optimizeCssModules } from 'vite-plugin-optimize-css-modules'
import Pages from 'vite-plugin-pages'
import webfontDownload from 'vite-plugin-webfont-dl'
import { fetchContributors } from './plugins/vite-fetch-contributors.ts'
import { i18nVirtualManifest } from './plugins/vite-i18n-virtual-manifest.ts'

// 构建戳：每次构建唯一，用于隔离静态资源缓存
const buildStamp = Date.now().toString(36)

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd())

  const TARGET = env.VITE_BACKEND_URL ?? 'http://localhost:8080'
  const current = new Date()

  const BANNER =
    `/* The GZ::CTF Project @${env.VITE_APP_GIT_NAME ?? 'unknown'}\n * \n` +
    ` * License   : GNU Affero General Public License v3.0 (Core)\n` +
    ` * License   : LicenseRef-GZCTF-Restricted (Restricted components)\n` +
    ` * Commit    : ${env.VITE_APP_GIT_SHA ?? 'Unofficial build version'}\n` +
    ` * Build     : ${env.VITE_APP_BUILD_TIMESTAMP ?? current.toISOString()}\n` +
    ` * Copyright (C) 2022-${current.getFullYear()} GZTimeWalker. All Rights Reserved.\n */`

  console.log(`Using backend URL: ${TARGET}`)

  return {
    server: {
      port: 63000,
      proxy: {
        '/api': TARGET,
        '/swagger': TARGET,
        '/assets': TARGET,
        '/hub': { target: TARGET.replace('http', 'ws'), ws: true },
        '/favicon.webp': TARGET,
      },
    },
    resolve: {
      tsconfigPaths: true,
    },
    preview: { port: 64000 },
    build: {
      outDir: 'build',
      assetsDir: 'static',
      cssMinify: 'esbuild',
      cssCodeSplit: false,
      chunkSizeWarningLimit: 2400,
      reportCompressedSize: true,
      rolldownOptions: {
        output: {
          hashCharacters: 'base36',
          // 每次构建把 chunk / 入口放到独立目录：旧部署的缓存文件永远不会被新入口引用，
          // 避免「同 URL 不同内容」导致的 "does not provide an export named ..." 报错。
          // assets（含 i18n json）保持原路径，前端用 /static/<file> 直接取。
          chunkFileNames: `static/${buildStamp}/[hash].js`,
          assetFileNames: 'static/[hash].[ext]',
          entryFileNames: `static/${buildStamp}/[name].[hash].js`,
        },
      },
    },
    html: { cspNonce: '%nonce%' },
    plugins: [
      react(),
      banner(BANNER),
      webfontDownload(
        [
          'https://fonts.googleapis.com/css2?family=JetBrains+Mono:ital,wght@0,100..800;1,100..800&family=Lexend:wght@100..900&display=swap',
        ],
        {
          injectAsStyleTag: false,
          async: false,
        }
      ),
      Pages({ dirs: [{ dir: './src/pages', baseRoute: '', filePattern: '**/*.tsx' }] }),
      i18nVirtualManifest(),
      fetchContributors(),
      optimizeCssModules(),
    ],
  }
})
