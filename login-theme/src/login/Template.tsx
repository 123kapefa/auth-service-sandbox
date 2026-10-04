import { kcSanitize } from "keycloakify/lib/kcSanitize";
import type { TemplateProps } from "keycloakify/login/TemplateProps";
import { getKcClsx } from "keycloakify/login/lib/kcClsx";
import { useInitialize } from "keycloakify/login/Template.useInitialize";
import { useEffect } from "react";
import { getBranding } from "./branding";
import type { I18n } from "./i18n";
import type { KcContext } from "./KcContext";

// Ошибка — бледно-красный блок с красной рамкой (не цвет проекта), предупреждение — пунктир и штриховка, успех — рамка, сведения —
// тонкая рамка.
const ALERT = {
  error: "border-danger bg-danger-muted text-danger",
  warning: "border-dashed border-primary bg-hatch",
  success: "border-primary",
  info: "border-border",
} as const;

/**
 * Каркас страниц входа: название, карточка с заголовком, сообщение Keycloak, форма, «другой способ», ссылки внизу,
 * выбор языка. Стили — Tailwind (classes.ts), без CSS Keycloak.
 */
export default function Template(props: TemplateProps<KcContext, I18n>) {
  const {
    displayInfo = false,
    displayMessage = true,
    headerNode,
    socialProvidersNode = null,
    infoNode = null,
    documentTitle,
    kcContext,
    i18n,
    doUseDefaultCss,
    classes,
    children,
  } = props;
  const { kcClsx } = getKcClsx({ doUseDefaultCss, classes });
  const { msg, msgStr, currentLanguage, enabledLanguages } = i18n;
  const { realm, auth, url, message, isAppInitiatedAction } = kcContext;
  // Название приложения (если у него своё) или проекта — из плагина; не загрузилось — displayName realm.
  const brandName = getBranding()?.name || realm.displayName || realm.name;

  useEffect(() => {
    document.title = documentTitle ?? msgStr("loginTitle", brandName);
  }, [documentTitle, msgStr, brandName]);

  const { isReadyToRender } = useInitialize({ kcContext, doUseDefaultCss });
  if (!isReadyToRender) {
    return null;
  }

  const showUsername = auth !== undefined && auth.showUsername && !auth.showResetCredentials;
  const logo = getBranding()?.logo;
  return (
    <div className={kcClsx("kcLoginClass")}>
      <div id="kc-header" className={kcClsx("kcHeaderClass")}>
        <div id="kc-header-wrapper" className={kcClsx("kcHeaderWrapperClass")}>
          {logo && <img id="kc-logo" src={logo} alt="" className="h-9 max-w-40 object-contain" />}
          <span>{brandName}</span>
        </div>
      </div>
      <div className={kcClsx("kcFormCardClass")}>
        <header className={kcClsx("kcFormHeaderClass")}>
          {showUsername ? (
            <div id="kc-username" className="flex items-center justify-between gap-2 text-sm">
              <span id="kc-attempted-username" className="font-medium">
                {auth.attemptedUsername}
              </span>
              <a
                id="reset-login"
                href={url.loginRestartFlowUrl}
                className="underline underline-offset-4 hover:text-muted-foreground"
              >
                {msg("restartLoginTooltip")}
              </a>
            </div>
          ) : (
            <h1 id="kc-page-title" className="text-2xl font-bold tracking-tight">
              {headerNode}
            </h1>
          )}
        </header>
        <div id="kc-content">
          {displayMessage &&
            message !== undefined &&
            (message.type !== "warning" || !isAppInitiatedAction) && (
              <div role="alert" className={`${kcClsx("kcAlertClass")} ${ALERT[message.type]}`}>
                <span
                  className={kcClsx("kcAlertTitleClass")}
                  dangerouslySetInnerHTML={{ __html: kcSanitize(message.summary) }}
                />
              </div>
            )}
          {children}
          {auth?.showTryAnotherWayLink && (
            <form
              id="kc-select-try-another-way-form"
              action={url.loginAction}
              method="post"
              className="mt-4 text-center text-sm"
            >
              <input type="hidden" name="tryAnotherWay" value="on" />
              <button
                type="submit"
                id="try-another-way"
                className="underline underline-offset-4 hover:text-muted-foreground"
              >
                {msg("doTryAnotherWay")}
              </button>
            </form>
          )}
          {socialProvidersNode}
          {displayInfo && (
            <div id="kc-info" className={kcClsx("kcSignUpClass")}>
              <div id="kc-info-wrapper" className={kcClsx("kcInfoAreaWrapperClass")}>
                {infoNode}
              </div>
            </div>
          )}
        </div>
      </div>
      {enabledLanguages.length > 1 && (
        <nav
          id="kc-locale"
          aria-label={msgStr("languages")}
          className="flex justify-center gap-3 text-xs text-muted-foreground"
        >
          {enabledLanguages.map(({ languageTag, label, href }) => (
            <a
              key={languageTag}
              href={href}
              aria-current={label === currentLanguage.label ? "true" : undefined}
              className="hover:text-foreground aria-[current=true]:font-semibold aria-[current=true]:text-foreground"
            >
              {label}
            </a>
          ))}
        </nav>
      )}
    </div>
  );
}
