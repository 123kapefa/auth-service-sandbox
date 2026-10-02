import { getKcClsx } from "keycloakify/login/lib/kcClsx";
import type { PageProps } from "keycloakify/login/pages/PageProps";
import type { I18n } from "../i18n";
import type { KcContext } from "../KcContext";

/** Вход по коду (плагин code-login, sandbox-code-login.ftl): код из SMS или письма, отправить ещё раз, назад к паролю. */
export default function CodeLogin(
  props: PageProps<Extract<KcContext, { pageId: "sandbox-code-login.ftl" }>, I18n>,
) {
  const { kcContext, i18n, doUseDefaultCss, Template, classes } = props;
  const { kcClsx } = getKcClsx({ doUseDefaultCss, classes });
  const { url, codeChannel, codeTarget, codeResendAfter, messagesPerField } = kcContext;
  const { msg, msgStr } = i18n;
  const byEmail = codeChannel === "EMAIL";

  return (
    <Template
      kcContext={kcContext}
      i18n={i18n}
      doUseDefaultCss={doUseDefaultCss}
      classes={classes}
      headerNode={msg("sandboxCodeTitle")}
    >
      <p id="sandbox-code-target" className="mb-4 text-sm text-muted-foreground">
        {msg(byEmail ? "sandboxCodeSentToEmail" : "sandboxCodeSentToSms", codeTarget)}
      </p>
      <form
        id="sandbox-code-form"
        className={kcClsx("kcFormClass")}
        action={url.loginAction}
        method="post"
        noValidate
      >
        <div className={kcClsx("kcFormGroupClass")}>
          <label htmlFor="code" className={kcClsx("kcLabelClass")}>
            {msg(byEmail ? "sandboxCodeLabelEmail" : "sandboxCodeLabelSms")}
          </label>
          <input
            id="code"
            name="code"
            className={kcClsx("kcInputClass")}
            autoComplete="one-time-code"
            inputMode="numeric"
            autoFocus
            aria-invalid={messagesPerField.existsError("code") || undefined}
          />
        </div>
        <div className={kcClsx("kcFormButtonsClass")}>
          <button
            id="kc-login"
            name="login"
            type="submit"
            className={kcClsx("kcButtonClass", "kcButtonPrimaryClass")}
          >
            {msgStr("doLogIn")}
          </button>
          <button
            id="sandbox-code-resend"
            name="resend"
            type="submit"
            className={kcClsx("kcButtonClass", "kcButtonSecondaryClass")}
          >
            {msgStr("sandboxCodeResend")}
          </button>
        </div>
      </form>
      {codeResendAfter !== "0" && (
        <p id="sandbox-code-resend-after" className="mt-3 text-sm text-muted-foreground">
          {msg("sandboxCodeResendIn", codeResendAfter)}
        </p>
      )}
      <p className="mt-4 text-center text-sm">
        <a
          id="sandbox-code-back"
          href={url.loginRestartFlowUrl}
          className="text-primary hover:underline"
        >
          {msg("sandboxCodeBackToPassword")}
        </a>
      </p>
    </Template>
  );
}
