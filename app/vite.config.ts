import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import electron from 'vite-plugin-electron/simple'

export default defineConfig({
  plugins: [
    react(),
    electron({
      main: { entry: 'electron/main.ts' },
      preload: {
        input: 'electron/preload.ts',
        // A sandboxed preload cannot be an ES module, so this one entry is emitted as CommonJS.
        vite: {
          build: {
            rollupOptions: {
              output: { format: 'cjs', entryFileNames: 'preload.cjs', inlineDynamicImports: true },
            },
          },
        },
      },
    }),
  ],
  // Relative, because the renderer is loaded from the filesystem rather than served.
  base: './',
  build: { outDir: 'dist', emptyOutDir: true },
})
