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
  "sandbox-code-login.ftl": {
    /** SMS или EMAIL — куда ушёл код. */
    codeChannel: "SMS" | "EMAIL";
    /** Куда отправлен код — как показать пользователю. */
    codeTarget: string;
    /** Через сколько секунд можно запросить новый код ("0" — уже можно). */
    codeResendAfter: string;
  };
};

export type KcContext = ExtendKcContext<KcContextExtension, KcContextExtensionPerPage>;
