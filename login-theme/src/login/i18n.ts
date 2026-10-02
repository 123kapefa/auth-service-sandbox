import { i18nBuilder } from "keycloakify/login";
import type { ThemeName } from "../kc.gen";

// Свои тексты (плагин, письмо с кодом, «доступ закрыт») и тексты realm для страниц входа. Только литералы:
// Keycloakify читает их при сборке и кладёт в messages_*.properties темы — их видят и сообщения сервера (setError).
const { useI18n, ofTypeI18n: _ofTypeI18n } = i18nBuilder
  .withThemeName<ThemeName>()
  .withCustomTranslations({
    ru: {
      usernameOrEmail: "Телефон или почта",
      sandboxCodeLoginDo: "Войти по коду",
      sandboxCodeLoginTargetRequired: "Для входа по коду введите телефон или почту.",
      sandboxCodeTitle: "Вход по коду",
      sandboxCodeSentToSms: "Отправили код в SMS на {0}.",
      sandboxCodeSentToEmail: "Отправили код письмом на {0}.",
      sandboxCodeLabelSms: "Код из SMS",
      sandboxCodeLabelEmail: "Код из письма",
      sandboxCodeResend: "Отправить код ещё раз",
      sandboxCodeResendIn: "Новый код можно запросить через {0} с.",
      sandboxCodeBackToPassword: "Войти по паролю",
      sandboxCodeWrong: "Неверный код. Осталось попыток: {0}.",
      sandboxCodeLocked: "Слишком много неверных попыток. Попробуйте через {0} с.",
      sandboxCodeExpired: "Код больше не действует — отправьте новый.",
      sandboxCodeTooEarly: "Код уже отправлен. Новый можно запросить через {0} с.",
      sandboxCodeSendFailed: "Не удалось отправить код. Попробуйте ещё раз позже.",
      sandboxCodeEmailSubject: "Код для входа",
      sandboxCodeEmailBody:
        "Ваш код для входа: {0}\n\nКод действует {1} мин. Если вы не входили, просто проигнорируйте письмо.",
      sandboxAppAccessDenied:
        "Доступ к этому приложению для вас закрыт. Обратитесь к администратору приложения.",
      // Тексты realm (localizationTexts) — тема в браузере их не подтягивает; те, что на страницах входа.
      showPassword: "Показать",
      hidePassword: "Скрыть",
      "passkey-doAuthenticate": "Войти с ключом доступа",
      "passkey-login-title": "Вход с ключом доступа",
      "passkey-available-authenticators": "Доступные ключи доступа",
      "passkey-unsupported-browser-text":
        "Этот браузер не поддерживает ключи доступа. Попробуйте другой или обратитесь к администратору.",
      "passkey-createdAt-label": "Создан",
      "passkey-autofill-select": "Выберите ключ доступа",
      "webauthn-login-title": "Вход с ключом доступа",
      "webauthn-available-authenticators": "Доступные ключи доступа",
      "webauthn-doAuthenticate": "Войти с ключом доступа",
      "webauthn-display-name": "Ключ доступа",
      "webauthn-help-text": "Войдите с помощью ключа доступа.",
      "webauthn-passwordless-display-name": "Ключ доступа",
      "webauthn-passwordless-help-text":
        "Вход без пароля: отпечаток пальца, лицо или PIN-код устройства.",
      "webauthn-registration-init-label": "Ключ доступа (метка по умолчанию)",
      "webauthn-error-registration": "Не удалось зарегистрировать ключ доступа.<br /> {0}",
      "webauthn-error-api-get": "Не удалось войти с ключом доступа.<br /> {0}",
      "webauthn-error-user-not-found": "Ключ доступа принадлежит неизвестному пользователю.",
    },
    en: {
      usernameOrEmail: "Phone or email",
      sandboxCodeLoginDo: "Sign in with a code",
      sandboxCodeLoginTargetRequired: "Enter a phone number or email to sign in with a code.",
      sandboxCodeTitle: "Sign in with a code",
      sandboxCodeSentToSms: "We sent a code by SMS to {0}.",
      sandboxCodeSentToEmail: "We sent a code by email to {0}.",
      sandboxCodeLabelSms: "Code from SMS",
      sandboxCodeLabelEmail: "Code from email",
      sandboxCodeResend: "Send the code again",
      sandboxCodeResendIn: "You can request a new code in {0} s.",
      sandboxCodeBackToPassword: "Sign in with a password",
      sandboxCodeWrong: "Wrong code. Attempts left: {0}.",
      sandboxCodeLocked: "Too many wrong attempts. Try again in {0} s.",
      sandboxCodeExpired: "The code is no longer valid — send a new one.",
      sandboxCodeTooEarly: "The code has already been sent. You can request a new one in {0} s.",
      sandboxCodeSendFailed: "Could not send the code. Please try again later.",
      sandboxCodeEmailSubject: "Your sign-in code",
      sandboxCodeEmailBody:
        "Your sign-in code: {0}\n\nThe code is valid for {1} min. If you did not try to sign in, just ignore this email.",
      sandboxAppAccessDenied:
        "Your access to this application is closed. Please contact the application administrator.",
      showPassword: "Show",
      hidePassword: "Hide",
      "passkey-doAuthenticate": "Sign in with a passkey",
      "passkey-login-title": "Sign in with a passkey",
      "passkey-available-authenticators": "Available passkeys",
      "passkey-unsupported-browser-text":
        "This browser does not support passkeys. Try another one or contact your administrator.",
      "passkey-createdAt-label": "Created",
      "passkey-autofill-select": "Select a passkey",
      "webauthn-login-title": "Sign in with a passkey",
      "webauthn-available-authenticators": "Available passkeys",
      "webauthn-doAuthenticate": "Sign in with a passkey",
      "webauthn-display-name": "Passkey",
      "webauthn-help-text": "Sign in with a passkey.",
      "webauthn-passwordless-display-name": "Passkey",
      "webauthn-passwordless-help-text": "Passwordless sign-in: fingerprint, face or device PIN.",
      "webauthn-registration-init-label": "Passkey (default label)",
      "webauthn-error-registration": "Failed to register the passkey.<br /> {0}",
      "webauthn-error-api-get": "Failed to sign in with the passkey.<br /> {0}",
      "webauthn-error-user-not-found": "The passkey belongs to an unknown user.",
    },
  })
  .build();

type I18n = typeof _ofTypeI18n;

export { useI18n, type I18n };
