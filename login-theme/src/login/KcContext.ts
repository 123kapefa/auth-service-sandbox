import type { ExtendKcContext } from "keycloakify/login";
import type { KcEnvName, ThemeName } from "../kc.gen";

export type KcContextExtension = {
  themeName: ThemeName;
  properties: Record<KcEnvName, string> & {};
};

/**
 * Свои страницы плагина (code-login): ключ — имя шаблона из createForm(...), поля — атрибуты, которые ставит
 * аутентификатор (form.setAttribute). Keycloakify по ключу собирает .ftl в тему.
 */
export type KcContextExtensionPerPage = {
  "platform-code-login.ftl": {
    /** SMS или EMAIL — куда ушёл код. */
    codeChannel: "SMS" | "EMAIL";
    /** Куда отправлен код — как показать пользователю. */
    codeTarget: string;
    /** Через сколько секунд можно запросить новый код ("0" — уже можно). */
    codeResendAfter: string;
    /** Сколько цифр в коде (политика плагина). */
    codeLength: string;
    /** "true" — следующий код уйдёт только с капчей (поле smart-token в форме). Экран капчи — 10.8. */
    captchaRequired?: string;
    /** Ключ клиента Яндекс SmartCaptcha (нет — капча не настроена). */
    captchaSiteKey?: string;
  };
};

export type KcContext = ExtendKcContext<KcContextExtension, KcContextExtensionPerPage>;
