import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// Сервис входа: Docker — 8080, dotnet run — 5080 (AUTH_API=http://localhost:5080 npm run dev).
const api = process.env.AUTH_API ?? "http://localhost:8080";

// Админка раздаётся сервисом входа на /admin — и в образе, и в dev (прокси на сервис).
export default defineConfig({
  base: "/admin/",
  plugins: [react({ babel: { plugins: ["babel-plugin-react-compiler"] } }), tailwindcss()],
  resolve: {
    alias: { "@": path.resolve(import.meta.dirname, "./src") },
  },
  server: {
    port: 5173,
    strictPort: true,
    // Тот же origin, что у страницы: cookie сервиса (accessToken, refreshToken) уходят с запросами к API.
    proxy: {
      "/auth": { target: api, changeOrigin: false },
      "/me": { target: api, changeOrigin: false },
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
  },
});
