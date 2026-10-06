import { kcSanitize } from "keycloakify/lib/kcSanitize";
import { getKcClsx, type KcClsx } from "keycloakify/login/lib/kcClsx";
import { useScript } from "keycloakify/login/pages/Login.useScript";
import type { PageProps } from "keycloakify/login/pages/PageProps";
import { useIsPasswordRevealed } from "keycloakify/tools/useIsPasswordRevealed";
import { useEffect, type ReactNode } from "react";
import type { I18n } from "../i18n";
import type { KcContext } from "../KcContext";
import { LoginInput } from "../components/LoginInput";
import { ProviderIcon } from "../components/ProviderIcon";
import { useSubmitting } from "../lib/use-submitting";

/**
 * Вход (login.ftl) в два шага (плагин code-login, PasswordLoginForm, атрибут loginStep):
 * - login — телефон или почта → «Продолжить»; тут же ключ доступа и внешний вход;
 * - password — что ввели и «Изменить», пароль сразу, ниже код на то, что ввели, и ключ доступа (так и для незнакомого
 *   логина — по экрану не понять, есть ли аккаунт);
 * - methods — человек выключил пароль: включённые способы по цене (ключ доступа → код на почту → звонок).
 * Оформление шагов — 10.8. Без loginStep — прежняя форма в один шаг:
 * телефон или почта и пароль; «Войти по коду» — тот же логин, без пароля;
 * «Войти с ключом доступа» — passkey; ниже — «Или войдите через»: значки Госуслуг и Яндекс ID в ряд (провайдеры realm). Номер
 * показывается как +7 (999) 000-00-01 (с 8 и 10 цифр — тоже +7), уходит цифрами скрытым полем username.
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
    social,
    loginStep,
    loginMethods,
    loginDisplay,
    loginEmailHint,
    loginPhoneHint,
    loginAutoStart,
  } = kcContext;
  const { msg, msgStr } = i18n;
  const { submitting, markSubmitting } = useSubmitting();
  const webAuthnButtonId = "authenticateWebAuthnButton";
  const hasError = messagesPerField.existsError("username", "password");
  // Подтверждение пароля при привязке Госуслуг или Яндекс ID к существующей учётной записи (логин уже известен):
  // только пароль — без «Войти по коду» и без кнопок внешнего входа.
  const methods = (loginMethods ?? "").split(",").filter(Boolean);
  // Внешний вход — на шаге 1 и когда другого способа у человека нет.
  const showExternal =
    !usernameHidden &&
    (loginStep === undefined ||
      loginStep === "login" ||
      (loginStep === "methods" && methods.length === 0));
  const externalProviders = showExternal ? (social?.providers ?? []) : [];

  useScript({ webAuthnButtonId, kcContext, i18n });

  // Единственный способ — ключ доступа: открываем его сам. Кнопка появляется не сразу (см. PasswordField).
  useEffect(() => {
    if (loginAutoStart !== "passkey") return;
    let tries = 0;
    const timer = window.setInterval(() => {
      const button = document.getElementById(webAuthnButtonId);
      if (button || ++tries > 20) {
        window.clearInterval(timer);
        button?.click();
      }
    }, 100);
    return () => window.clearInterval(timer);
  }, [loginAutoStart]);

  const passkeyNode = enableWebAuthnConditionalUI && (
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
  );

  const errorNode = hasError && (
    <span
      id="input-error"
      role="alert"
      className={kcClsx("kcInputErrorMessageClass")}
      dangerouslySetInnerHTML={{
        __html: kcSanitize(messagesPerField.getFirstError("username", "password")),
      }}
    />
  );

  // Что ввели на шаге 1 и «Изменить» — назад к шагу 1. Кнопка стоит в форме входа, но отправляет свою пустую форму:
  // Enter в поле пароля (и менеджер паролей) жмёт первую кнопку своей формы — это должна быть «Войти», не «Изменить».
  const shownLogin = loginDisplay !== undefined && (
    <div className="flex items-center justify-between gap-3 text-sm">
      <span id="platform-login-display" className="font-medium">
        {loginDisplay}
      </span>
      <button
        id="platform-login-change"
        name="changeLogin"
        type="submit"
        form="platform-login-change-form"
        formNoValidate
        className="underline underline-offset-4 hover:text-muted-foreground"
      >
        {msgStr("platformLoginChange")}
      </button>
    </div>
  );
  const changeLoginForm = loginDisplay !== undefined && (
    <form id="platform-login-change-form" action={url.loginAction} method="post" noValidate hidden />
  );

  const codeButtons = (primary: boolean) =>
    methods
      .filter((method) => method === "email-code" || method === "phone-code")
      .map((method) => (
        <button
          key={method}
          id={`platform-code-login-${method}`}
          name="codeLogin"
          value={method}
          type="submit"
          disabled={submitting}
          className={kcClsx(
            "kcButtonClass",
            primary ? "kcButtonPrimaryClass" : "kcButtonSecondaryClass",
          )}
        >
          {loginStep === "methods"
            ? msgStr(
                method === "email-code" ? "platformLoginEmailCode" : "platformLoginPhoneCall",
                (method === "email-code" ? loginEmailHint : loginPhoneHint) ?? "",
              )
            : msgStr("platformCodeLoginDo")}
        </button>
      ));

  let body: ReactNode;
  if (loginStep === "login") {
    body = (
      <>
        <form
          id="kc-form-login"
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
              defaultValue={login.username ?? ""}
              autoComplete={enableWebAuthnConditionalUI ? "username webauthn" : "username"}
              invalid={hasError}
              describedBy="input-error"
            />
            {errorNode}
          </div>
          <div className={kcClsx("kcFormButtonsClass")}>
            <button
              id="platform-login-next"
              name="next"
              type="submit"
              disabled={submitting}
              className={kcClsx("kcButtonClass", "kcButtonPrimaryClass")}
            >
              {msgStr("platformLoginContinue")}
            </button>
          </div>
        </form>
        {passkeyNode}
      </>
    );
  } else if (loginStep === "password") {
    body = (
      <>
        <form
          id="kc-form-login"
          className={kcClsx("kcFormClass")}
          action={url.loginAction}
          method="post"
          noValidate
          onSubmit={markSubmitting}
        >
          {shownLogin}
          <div className={kcClsx("kcFormGroupClass")}>
            <div className="flex items-center justify-between">
              <label htmlFor="password" className={kcClsx("kcLabelClass")}>
                {msg("password")}
              </label>
              {realm.resetPasswordAllowed && (
                <a
                  href={url.loginResetCredentialsUrl}
                  className="text-sm underline underline-offset-4 hover:text-muted-foreground"
                >
                  {msg("doForgotPassword")}
                </a>
              )}
            </div>
            <PasswordField kcClsx={kcClsx} i18n={i18n} invalid={hasError} />
            {errorNode}
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
            {codeButtons(false)}
          </div>
        </form>
        {changeLoginForm}
        {methods.includes("passkey") && passkeyNode}
      </>
    );
  } else if (loginStep === "methods") {
    const codes = codeButtons(!methods.includes("passkey"));
    body = (
      <>
        <form
          id="kc-form-login"
          className={kcClsx("kcFormClass")}
          action={url.loginAction}
          method="post"
          noValidate
          onSubmit={markSubmitting}
        >
          {shownLogin}
          <p className="text-sm font-medium">{msg("platformLoginChooseMethod")}</p>
          {errorNode}
          {methods.length === 0 && (
            <p id="platform-login-no-methods" className="text-sm">
              {msg("platformLoginNoMethods")}
            </p>
          )}
        </form>
        {changeLoginForm}
        {/* По цене: ключ доступа, затем коды. */}
        {methods.includes("passkey") && passkeyNode}
        {codes.length > 0 && (
          <form
            id="platform-login-methods"
            className={`mt-2 ${kcClsx("kcFormButtonsClass")}`}
            action={url.loginAction}
            method="post"
            noValidate
            onSubmit={markSubmitting}
          >
            {codes}
          </form>
        )}
      </>
    );
  }

  return (
    <Template
      kcContext={kcContext}
      i18n={i18n}
      doUseDefaultCss={doUseDefaultCss}
      classes={classes}
      displayMessage={!hasError}
      headerNode={msg("loginAccountTitle")}
      displayInfo={realm.password && realm.registrationAllowed && !registrationDisabled}
      socialProvidersNode={
        externalProviders.length > 0 && (
          <div id="kc-social-providers" className="mt-6 grid gap-3">
            <p className="flex items-center gap-3 font-mono text-[11px] tracking-[0.1em] text-muted-foreground uppercase before:h-px before:flex-1 before:bg-border after:h-px after:flex-1 after:bg-border">
              {msg("identity-provider-login-label")}
            </p>
            {/* В один ряд, значками; название — подсказка и имя кнопки для экранного диктора. */}
            <ul className="flex justify-center gap-3">
              {externalProviders.map((provider) => (
                <li key={provider.alias}>
                  <a
                    id={`social-${provider.alias}`}
                    href={provider.loginUrl}
                    aria-label={provider.displayName}
                    title={provider.displayName}
                    className="flex size-12 items-center justify-center border border-primary bg-transparent transition-colors hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                  >
                    <ProviderIcon alias={provider.alias} name={provider.displayName} />
                  </a>
                </li>
              ))}
            </ul>
          </div>
        )
      }
      infoNode={
        <span>
          {msg("noAccount")}{" "}
          <a
            href={url.registrationUrl}
            className="underline underline-offset-4 hover:text-muted-foreground"
          >
            {msg("doRegister")}
          </a>
        </span>
      }
    >
      {body ?? (
        <>
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
                      className="text-sm underline underline-offset-4 hover:text-muted-foreground"
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
                {!usernameHidden && (
                  <button
                    id="platform-code-login"
                    name="codeLogin"
                    type="submit"
                    disabled={submitting}
                    className={kcClsx("kcButtonClass", "kcButtonSecondaryClass")}
                  >
                    {msgStr("platformCodeLoginDo")}
                  </button>
                )}
              </div>
            </form>
          )}
          {passkeyNode}
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
