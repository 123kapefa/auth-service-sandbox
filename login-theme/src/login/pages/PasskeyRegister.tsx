import { getKcClsx } from "keycloakify/login/lib/kcClsx";
import type { PageProps } from "keycloakify/login/pages/PageProps";
import { useEffect, useRef } from "react";
import type { I18n } from "../i18n";
import type { KcContext } from "../KcContext";
import { useSubmitting } from "../lib/use-submitting";

type Props = PageProps<
  Extract<KcContext, { pageId: "webauthn-register.ftl" | "webauthn-error.ftl" }>,
  I18n
>;

type RegisterModule = { registerByWebAuthn: (input: Record<string, unknown>) => Promise<void> };

/**
 * Добавление ключа доступа — страницы регистрации ключа Keycloak (webauthn-register / webauthn-error).
 * «Добавить ключ доступа» сразу открывает окно браузера (Face ID, отпечаток, PIN); не получилось — «Попробовать ещё раз».
 * Два случая:
 * <ul>
 *   <li>«Входите без кода» (атрибут platformPasskeyOffer, действие platform-passkey-offer, 10.6) — после входа по
 *       коду; «Не сейчас» — дальше без ключа (сервер запомнит и 30 дней не спросит);</li>
 *   <li>из кабинета (kc_action=webauthn-register-passwordless) — «Выполнить выход на других устройствах» и «Отмена»
 *       (назад в кабинет).</li>
 * </ul>
 */
export default function PasskeyRegister(props: Props) {
  const { kcContext, i18n, doUseDefaultCss, Template, classes } = props;
  const { kcClsx } = getKcClsx({ doUseDefaultCss, classes });
  const { url } = kcContext;
  const { msg, msgStr } = i18n;
  const { submitting, markSubmitting, setSubmitting } = useSubmitting();
  const failed = kcContext.pageId === "webauthn-error.ftl";
  const offer = kcContext.platformPasskeyOffer === "true";
  const transition = offer ? parseTransition(kcContext.platformSignInTransition) : undefined;
  const register = useRef<Promise<RegisterModule> | null>(null);

  // Скрипт Keycloak грузим заранее: окно ключа браузер откроет только сразу после нажатия.
  useEffect(() => {
    if (!failed) {
      register.current = import(
        /* @vite-ignore */ `${url.resourcesPath}/js/webauthnRegister.js`
      ) as Promise<RegisterModule>;
      register.current.catch(() => undefined);
    }
  }, [failed, url.resourcesPath]);

  const add = async () => {
    if (kcContext.pageId !== "webauthn-register.ftl" || !register.current) {
      return;
    }
    setSubmitting(true);
    const { registerByWebAuthn } = await register.current;
    // Keycloak спрашивает имя ключа окном prompt — называем сами.
    const prompt = window.prompt;
    window.prompt = () => msgStr("platformPasskeyLabel");
    try {
      await registerByWebAuthn({
        challenge: kcContext.challenge,
        userid: kcContext.userid,
        username: kcContext.username,
        signatureAlgorithms: kcContext.signatureAlgorithms,
        rpEntityName: kcContext.rpEntityName,
        rpId: kcContext.rpId,
        attestationConveyancePreference: kcContext.attestationConveyancePreference,
        authenticatorAttachment: kcContext.authenticatorAttachment,
        requireResidentKey: kcContext.requireResidentKey,
        residentKey: (kcContext as { residentKey?: string }).residentKey,
        userVerificationRequirement: kcContext.userVerificationRequirement,
        createTimeout: kcContext.createTimeout,
        excludeCredentialIds: kcContext.excludeCredentialIds,
        initLabel: msgStr("platformPasskeyLabel"),
        initLabelPrompt: "",
        errmsg: msgStr("webauthn-unsupported-browser-text"),
      });
    } finally {
      window.prompt = prompt;
    }
  };

  return (
    <Template
      kcContext={kcContext}
      i18n={i18n}
      doUseDefaultCss={doUseDefaultCss}
      classes={classes}
      displayMessage={false}
      headerNode={
        <span className="grid gap-6">
          <span
            aria-hidden
            className="flex size-16 items-center justify-center border-2 border-primary bg-background font-mono text-sm font-semibold shadow-hard"
          >
            ID
          </span>
          {transition
            ? msg("platformSignInTransitionTitle")
            : msg(offer ? "platformPasskeyOfferTitle" : "platformPasskeyAddTitle")}
        </span>
      }
    >
      <p id="platform-passkey-offer-text" className="mb-4 text-sm">
        {transition
          ? msg("platformSignInTransitionText", msgStr(transition.method), transition.until)
          : msg(offer ? "platformPasskeyOfferText" : "platformPasskeyAddText")}
      </p>
      {failed && (
        <p id="input-error" role="alert" className={`mb-4 ${kcClsx("kcInputErrorMessageClass")}`}>
          {msg(offer ? "platformPasskeyOfferFailed" : "platformPasskeyAddFailed")}
        </p>
      )}
      {failed ? (
        <form
          id="platform-passkey-retry"
          action={url.loginAction}
          method="post"
          onSubmit={markSubmitting}
        >
          <input type="hidden" name="isSetRetry" value="retry" />
          <button
            id="platform-passkey-add"
            type="submit"
            disabled={submitting}
            className={kcClsx("kcButtonClass", "kcButtonPrimaryClass", "kcButtonBlockClass")}
          >
            {msgStr("platformPasskeyOfferRetry")}
          </button>
        </form>
      ) : (
        <form id="register" action={url.loginAction} method="post">
          {[
            "clientDataJSON",
            "attestationObject",
            "publicKeyCredentialId",
            "authenticatorLabel",
            "transports",
            "authenticatorAttachment",
            "error",
          ].map((name) => (
            <input key={name} type="hidden" id={name} name={name} />
          ))}
          {!offer && (
            // Как у стандартной страницы Keycloak: ключ добавлен — завершить остальные сеансы.
            <label className="mb-4 flex items-center text-sm">
              <input
                id="logout-sessions"
                name="logout-sessions"
                value="on"
                type="checkbox"
                className={kcClsx("kcCheckboxInputClass")}
              />
              {msg("logoutOtherSessions")}
            </label>
          )}
          <button
            id="platform-passkey-add"
            type="button"
            disabled={submitting}
            onClick={() => void add().catch(() => setSubmitting(false))}
            className={kcClsx("kcButtonClass", "kcButtonPrimaryClass", "kcButtonBlockClass")}
          >
            {msgStr("platformPasskeyOfferAdd")}
          </button>
        </form>
      )}
      {(offer || kcContext.isAppInitiatedAction) && (
        <form action={url.loginAction} method="post" className="mt-2" onSubmit={markSubmitting}>
          <button
            id="platform-passkey-skip"
            name={offer ? "skip" : "cancel-aia"}
            value="true"
            type="submit"
            disabled={submitting}
            className={kcClsx("kcButtonClass", "kcButtonSecondaryClass", "kcButtonBlockClass")}
          >
            {offer ? msgStr("platformPasskeyOfferSkip") : msgStr("doCancel")}
          </button>
        </form>
      )}
      {offer && (
        <p className="mt-4 text-center text-sm text-muted-foreground">
          {msg(transition ? "platformSignInTransitionLater" : "platformPasskeyOfferLater")}
        </p>
      )}
    </Template>
  );
}

const TRANSITION_METHODS = {
  phone: "platformSignInTransitionPhone",
  email: "platformSignInTransitionEmail",
  password: "platformSignInTransitionPassword",
} as const;

/** «phone:2026-11-05» → какой способ и до какого числа (05.11.2026); непонятное — без переходного текста. */
function parseTransition(value: string | undefined) {
  const [method, until] = value?.split(":") ?? [];
  const date = /^(\d{4})-(\d{2})-(\d{2})$/.exec(until ?? "");
  if (!date || !method || !(method in TRANSITION_METHODS)) {
    return undefined;
  }
  return {
    method: TRANSITION_METHODS[method as keyof typeof TRANSITION_METHODS],
    until: `${date[3]}.${date[2]}.${date[1]}`,
  };
}
