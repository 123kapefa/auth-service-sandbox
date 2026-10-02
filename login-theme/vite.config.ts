import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { keycloakify } from "keycloakify/vite-plugin";
import { defineConfig } from "vitest/config";

// Тема входа Keycloak на React (Keycloakify). npm run build-keycloak-theme — jar темы (нужны Maven и Java: собирается
// в Dockerfile Keycloak). npm run dev — страницы на макетах контекста Keycloak, без самого Keycloak.
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    keycloakify({
      themeName: "sandbox",
      accountThemeImplementation: "none",
      // Keycloak 26 — «все остальные версии»; для 22–25 сборка не нужна.
      keycloakVersionTargets: {
        "22-to-25": false,
        "all-other-versions": "sandbox-login-theme.jar",
      },
    }),
  ],
  // 5174 — чтобы не занять порт админки (frontend/, 5173).
  server: { port: 5174, strictPort: true },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
  },
});
