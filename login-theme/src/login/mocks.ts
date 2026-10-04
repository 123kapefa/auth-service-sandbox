import { createGetKcContextMock } from "keycloakify/login/KcContext";
import type { KcContextExtension, KcContextExtensionPerPage } from "./KcContext";
import { kcEnvDefaults } from "../kc.gen";

/** Макеты контекста Keycloak: npm run dev (?page=login.ftl) и тесты — страницы без Keycloak. */
export const { getKcContextMock } = createGetKcContextMock({
  kcContextExtension: {
    themeName: "platform",
    properties: { ...kcEnvDefaults },
  } satisfies KcContextExtension,
  kcContextExtensionPerPage: {
    "platform-code-login.ftl": {
      codeChannel: "SMS" as "SMS" | "EMAIL",
      codeTarget: "+7 999 *** ** 01",
      codeResendAfter: "42",
      codeLength: "6",
    },
  } satisfies KcContextExtensionPerPage,
  overrides: {
    // Как в realm: русский по умолчанию и английский.
    locale: {
      currentLanguageTag: "ru",
      supported: [
        { languageTag: "ru", label: "Русский", url: "?kc_locale=ru" },
        { languageTag: "en", label: "English", url: "?kc_locale=en" },
      ],
    },
    realm: { displayName: "Auth Sandbox", registrationEmailAsUsername: false },
  },
  // Как в демо-проекте (realm sandbox): вход по телефону или почте, сброс пароля, без регистрации и «запомнить меня».
  overridesPerPage: {
    "login.ftl": {
      realm: {
        loginWithEmailAllowed: true,
        registrationAllowed: false,
        resetPasswordAllowed: true,
        rememberMe: false,
      },
    },
    "login-reset-password.ftl": { realm: { loginWithEmailAllowed: true } },
    "webauthn-authenticate.ftl": { realm: { registrationAllowed: false } },
  },
});
