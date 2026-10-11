import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  css: {
    preprocessorOptions: {
      // quietDeps mutes the warnings of Bootstrap's own Sass. Bootstrap 5.3 has no modules, so our entry has to
      // @import it, and Dart Sass would warn about that on every build.
      scss: {
        quietDeps: true,
        silenceDeprecations: ['import'],
      },
    },
  },
  build: {
    // Bootstrap's Sass has no vendor prefixes (its own build adds them). Safari and iOS before 15.4 need
    // -webkit-appearance to draw Bootstrap's fields, selects and checks; Hyper's compiled sheet had it.
    cssTarget: ['chrome107', 'edge107', 'firefox104', 'safari15', 'ios15'],
    rollupOptions: {
      onwarn(warning, warn) {
        const isSignalrPureAnnotationWarning =
          warning.message?.includes('contains an annotation that Rollup cannot interpret')
          && warning.message?.includes('@microsoft/signalr')

        if (isSignalrPureAnnotationWarning) {
          return
        }

        warn(warning)
      },
      output: {
        manualChunks(id) {
          if (!id.includes('node_modules')) return undefined

          const packagePath = id.split('node_modules/')[1]
          if (!packagePath) return undefined

          const parts = packagePath.split('/')
          const packageName = parts[0].startsWith('@') && parts.length > 1
            ? `${parts[0]}/${parts[1]}`
            : parts[0]

          if (packageName.startsWith('react') || packageName === 'scheduler') {
            return 'vendor-react'
          }

          if (packageName.startsWith('@tanstack/')) {
            return 'vendor-query'
          }

          if (packageName === '@microsoft/signalr') {
            return 'vendor-signalr'
          }

          if (packageName.startsWith('bootstrap') || packageName.startsWith('react-bootstrap')) {
            return 'vendor-bootstrap'
          }

          return undefined
        },
      },
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5203',
        changeOrigin: true,
      },
      '/uploads': {
        target: 'http://localhost:5203',
        changeOrigin: true,
      },
      '/hubs': {
        target: 'http://localhost:5203',
        changeOrigin: true,
        ws: true,
      },
    },
  },
})
