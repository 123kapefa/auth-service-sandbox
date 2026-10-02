import { createGetKcContextMock } from "keycloakify/login/KcContext";
import type { KcContextExtension, KcContextExtensionPerPage } from "./KcContext";
import { kcEnvDefaults } from "../kc.gen";

/** Макеты контекста Keycloak: npm run dev (?page=login.ftl) и тесты — страницы без Keycloak. */
export const { getKcContextMock } = createGetKcContextMock({
  kcContextExtension: {
    themeName: "sandbox",
    properties: { ...kcEnvDefaults },
  } satisfies KcContextExtension,
  kcContextExtensionPerPage: {
    "sandbox-code-login.ftl": {
      codeChannel: "SMS" as "SMS" | "EMAIL",
      codeTarget: "+7 999 *** ** 01",
      codeResendAfter: "42",
    },
  } satisfies KcContextExtensionPerPage,
  overrides: {
    locale: { currentLanguageTag: "ru" },
    realm: { displayName: "Auth Sandbox" },
  },
  overridesPerPage: {},
});
