import { kcSanitize } from "keycloakify/lib/kcSanitize";
import { getKcClsx } from "keycloakify/login/lib/kcClsx";
import type { PageProps } from "keycloakify/login/pages/PageProps";
import type { I18n } from "../i18n";
import type { KcContext } from "../KcContext";
import { LoginInput } from "../components/LoginInput";
import { useSubmitting } from "../lib/use-submitting";

/**
 * «Забыли пароль?» (login-reset-password.ftl): то же поле «Телефон или почта», что на входе, — номер уходит цифрами,
 * иначе Keycloak не найдёт пользователя по «8 999 000-00-01» (и, не раскрывая этого, просто не пришлёт письмо).
 */
export default function LoginResetPassword(
  props: PageProps<Extract<KcContext, { pageId: "login-reset-password.ftl" }>, I18n>,
) {
  const { kcContext, i18n, doUseDefaultCss, Template, classes } = props;
  const { kcClsx } = getKcClsx({ doUseDefaultCss, classes });
  const { url, realm, auth, messagesPerField } = kcContext;
  const { msg, msgStr } = i18n;
  const { submitting, markSubmitting } = useSubmitting();
  const hasError = messagesPerField.existsError("username");

  return (
    <Template
      kcContext={kcContext}
      i18n={i18n}
      doUseDefaultCss={doUseDefaultCss}
      classes={classes}
      displayInfo
      displayMessage={!hasError}
      infoNode={msg(realm.duplicateEmailsAllowed ? "emailInstructionUsername" : "emailInstruction")}
      headerNode={msg("emailForgotTitle")}
    >
      <form
        id="kc-reset-password-form"
        className={kcClsx("kcFormClass")}
        action={url.loginAction}
        method="post"
        noValidate
        onSubmit={markSubmitting}
      >
        <div className={kcClsx("kcFormGroupClass")}>
          <label htmlFor="username" className={kcClsx("kcLabelClass")}>
            {msg("usernameOrEmail")}
          </label>
          <LoginInput
            className={kcClsx("kcInputClass")}
            defaultValue={auth.attemptedUsername ?? ""}
            autoComplete="username"
            invalid={hasError}
            describedBy="input-error-username"
          />
          {hasError && (
            <span
              id="input-error-username"
              role="alert"
              className={kcClsx("kcInputErrorMessageClass")}
              dangerouslySetInnerHTML={{ __html: kcSanitize(messagesPerField.get("username")) }}
            />
          )}
        </div>
        <div className={kcClsx("kcFormButtonsClass")}>
          <button
            id="kc-reset-password-submit"
            type="submit"
            disabled={submitting}
            className={kcClsx("kcButtonClass", "kcButtonPrimaryClass")}
          >
            {msgStr("doSubmit")}
          </button>
        </div>
        <a
          href={url.loginUrl}
          className="block text-center text-sm underline underline-offset-4 hover:text-muted-foreground"
        >
          {msg("backToLogin")}
        </a>
      </form>
    </Template>
  );
}
