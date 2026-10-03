import { kcSanitize } from "keycloakify/lib/kcSanitize";
import { getKcClsx, type KcClsx } from "keycloakify/login/lib/kcClsx";
import { useScript } from "keycloakify/login/pages/Login.useScript";
import type { PageProps } from "keycloakify/login/pages/PageProps";
import { useIsPasswordRevealed } from "keycloakify/tools/useIsPasswordRevealed";
import type { I18n } from "../i18n";
import type { KcContext } from "../KcContext";
import { LoginInput } from "../components/LoginInput";
import { useSubmitting } from "../lib/use-submitting";

/**
 * Вход (login.ftl): телефон или почта и пароль; «Войти по коду» — тот же логин, без пароля (плагин code-login);
 * «Войти с ключом доступа» — passkey. Номер показывается как +7 (999) 000-00-01 (с 8 и 10 цифр — тоже +7), уходит
 * цифрами скрытым полем username.
 */
export default function Login(props: PageProps<Extract<KcContext, { pageId: "login.ftl" }>, I18n>) {
  const { kcContext, i18n, doUseDefaultCss, Template, classes } = props;
  const { kcClsx } = getKcClsx({ doUseDefaultCss, classes });
  const {
    realm,
    url,
    usernameHidden,
    login,
    auth,
    registrationDisabled,
    messagesPerField,
    enableWebAuthnConditionalUI,
    authenticators,
  } = kcContext;
  const { msg, msgStr } = i18n;
  const { submitting, markSubmitting } = useSubmitting();
  const webAuthnButtonId = "authenticateWebAuthnButton";
  const hasError = messagesPerField.existsError("username", "password");

  useScript({ webAuthnButtonId, kcContext, i18n });

  return (
    <Template
      kcContext={kcContext}
      i18n={i18n}
      doUseDefaultCss={doUseDefaultCss}
      classes={classes}
      displayMessage={!hasError}
      headerNode={msg("loginAccountTitle")}
      displayInfo={realm.password && realm.registrationAllowed && !registrationDisabled}
      infoNode={
        <span>
          {msg("noAccount")}{" "}
          <a href={url.registrationUrl} className="text-primary hover:underline">
            {msg("doRegister")}
          </a>
        </span>
      }
    >
      {realm.password && (
        <form
          id="kc-form-login"
          className={kcClsx("kcFormClass")}
          action={url.loginAction}
          method="post"
          noValidate
          onSubmit={markSubmitting}
        >
          {!usernameHidden && (
            <div className={kcClsx("kcFormGroupClass")}>
              <label htmlFor="username" className={kcClsx("kcLabelClass")}>
                {msg("usernameOrEmail")}
              </label>
              <LoginInput
                className={kcClsx("kcInputClass")}
                defaultValue={login.username ?? ""}
                autoComplete={enableWebAuthnConditionalUI ? "username webauthn" : "username"}
                invalid={hasError}
                describedBy="input-error"
              />
            </div>
          )}
          <div className={kcClsx("kcFormGroupClass")}>
            <div className="flex items-center justify-between">
              <label htmlFor="password" className={kcClsx("kcLabelClass")}>
                {msg("password")}
              </label>
              {realm.resetPasswordAllowed && (
                <a
                  href={url.loginResetCredentialsUrl}
                  className="text-sm text-primary hover:underline"
                >
                  {msg("doForgotPassword")}
                </a>
              )}
            </div>
            <PasswordField kcClsx={kcClsx} i18n={i18n} invalid={hasError} />
            {hasError && (
              <span
                id="input-error"
                role="alert"
                className={kcClsx("kcInputErrorMessageClass")}
                dangerouslySetInnerHTML={{
                  __html: kcSanitize(messagesPerField.getFirstError("username", "password")),
                }}
              />
            )}
          </div>
          {realm.rememberMe && !usernameHidden && (
            <label className="flex items-center text-sm">
              <input
                id="rememberMe"
                name="rememberMe"
                type="checkbox"
                className={kcClsx("kcCheckboxInputClass")}
                defaultChecked={!!login.rememberMe}
              />
              {msg("rememberMe")}
            </label>
          )}
          <input
            type="hidden"
            id="id-hidden-input"
            name="credentialId"
            value={auth.selectedCredential ?? ""}
          />
          <div className={kcClsx("kcFormButtonsClass")}>
            <button
              id="kc-login"
              name="login"
              type="submit"
              disabled={submitting}
              className={kcClsx("kcButtonClass", "kcButtonPrimaryClass")}
            >
              {msgStr("doLogIn")}
            </button>
            {/* Вход по коду: пароль не нужен, номер или почта — из поля выше. */}
            <button
              id="platform-code-login"
              name="codeLogin"
              type="submit"
              disabled={submitting}
              className={kcClsx("kcButtonClass", "kcButtonSecondaryClass")}
            >
              {msgStr("platformCodeLoginDo")}
            </button>
          </div>
        </form>
      )}
      {enableWebAuthnConditionalUI && (
        <>
          <form id="webauth" action={url.loginAction} method="post">
            <input type="hidden" id="clientDataJSON" name="clientDataJSON" />
            <input type="hidden" id="authenticatorData" name="authenticatorData" />
            <input type="hidden" id="signature" name="signature" />
            <input type="hidden" id="credentialId" name="credentialId" />
            <input type="hidden" id="userHandle" name="userHandle" />
            <input type="hidden" id="error" name="error" />
          </form>
          {authenticators !== undefined && authenticators.authenticators.length !== 0 && (
            <form id="authn_select" className="hidden">
              {authenticators.authenticators.map((authenticator) => (
                <input
                  key={authenticator.credentialId}
                  type="hidden"
                  name="authn_use_chk"
                  readOnly
                  value={authenticator.credentialId}
                />
              ))}
            </form>
          )}
          <button
            id={webAuthnButtonId}
            type="button"
            className={`mt-2 ${kcClsx("kcButtonClass", "kcButtonDefaultClass")}`}
          >
            {msgStr("passkey-doAuthenticate")}
          </button>
        </>
      )}
    </Template>
  );
}

/**
 * Пароль с кнопкой «Показать». Хук показа ищет поле на странице — поэтому он здесь, а не в самой странице: шаблон
 * рисует содержимое не сразу (сначала готовит страницу), и поля в этот момент ещё нет.
 */
function PasswordField({
  kcClsx,
  i18n,
  invalid,
}: {
  kcClsx: KcClsx;
  i18n: I18n;
  invalid: boolean;
}) {
  const { isPasswordRevealed, toggleIsPasswordRevealed } = useIsPasswordRevealed({
    passwordInputId: "password",
  });
  return (
    <div className={kcClsx("kcInputGroup")}>
      <input
        id="password"
        name="password"
        type="password"
        className={kcClsx("kcInputClass")}
        autoComplete="current-password"
        aria-invalid={invalid || undefined}
        aria-describedby={invalid ? "input-error" : undefined}
      />
      <button
        type="button"
        className={kcClsx("kcFormPasswordVisibilityButtonClass")}
        aria-controls="password"
        onClick={toggleIsPasswordRevealed}
      >
        {i18n.msgStr(isPasswordRevealed ? "hidePassword" : "showPassword")}
      </button>
    </div>
  );
}
