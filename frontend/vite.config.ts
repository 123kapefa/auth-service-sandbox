import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { type Plugin } from "vite";
import { defineConfig } from "vitest/config";

// Сервис входа: Docker — 8080, dotnet run — 5080 (AUTH_API=http://localhost:5080 npm run dev).
const api = process.env.AUTH_API ?? "http://localhost:8080";

// Два приложения одного проекта (общие shared, entities, сборка): админка доступа на /admin и личный кабинет на
// /account. Какое — по режиму Vite: `vite --mode account`, `vite build --mode account`; иначе — админка.
const apps = {
  admin: { base: "/admin/", html: "index.html", port: 5173 },
  account: { base: "/account/", html: "account.html", port: 5174 },
} as const;

/** Страница приложения — не index.html корня: в dev отдаём её на адресах приложения, в сборке — кладём как index.html. */
function appEntry(base: string, html: string): Plugin {
  return {
    name: "app-entry",
    // После сборки HTML самим Vite: файл страницы появляется в bundle только на его шаге.
    enforce: "post",
    configureServer(server) {
      server.middlewares.use((request, _response, next) => {
        // Переход браузера (ждёт HTML) на адрес приложения — его страница; модули и файлы Vite просят не HTML.
        const url = request.url ?? "";
        const inApp = url.startsWith(base) || url.split("?")[0] === base.slice(0, -1);
        if (inApp && request.headers.accept?.includes("text/html")) {
          request.url = `${base}${html}`;
        }

        next();
      });
    },
    generateBundle(_options, bundle) {
      const page = bundle[html];
      if (page) {
        page.fileName = "index.html";
      }
    },
  };
}

export default defineConfig(({ mode }) => {
  const app = mode === "account" ? apps.account : apps.admin;
  return {
    base: app.base,
    plugins: [
      react({ babel: { plugins: ["babel-plugin-react-compiler"] } }),
      tailwindcss(),
      ...(app.html === "index.html" ? [] : [appEntry(app.base, app.html)]),
    ],
    resolve: {
      alias: { "@": path.resolve(import.meta.dirname, "./src") },
    },
    build: {
      outDir: `dist${app.base}`,
      emptyOutDir: true,
      // Шрифты — только файлами: CSP сервиса не пускает data: (мелкие наборы символов Vite иначе встроил бы в CSS).
      assetsInlineLimit: (file) => (/\.woff2?$/.test(file) ? false : undefined),
      rollupOptions: { input: path.resolve(import.meta.dirname, app.html) },
    },
    server: {
      port: app.port,
      strictPort: true,
      // Тот же origin, что у страницы: cookie сервиса (accessToken, refreshToken) уходят с запросами к API.
      proxy: {
        "/auth": { target: api, changeOrigin: false },
        "/me": { target: api, changeOrigin: false },
        "/security": { target: api, changeOrigin: false },
      },
    },
    test: {
      environment: "jsdom",
      globals: true,
      setupFiles: ["./src/test/setup.ts"],
      include: ["src/**/*.test.{ts,tsx}"],
    },
  };
});
