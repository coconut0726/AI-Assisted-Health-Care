import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Relative base + HashRouter lets the build run from any sub-path (e.g. GitHub Pages).
export default defineConfig({
  plugins: [react()],
  base: './',
});
