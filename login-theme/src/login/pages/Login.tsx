import { kcSanitize } from "keycloakify/lib/kcSanitize";
import { getKcClsx, type KcClsx } from "keycloakify/login/lib/kcClsx";
import { useScript } from "keycloakify/login/pages/Login.useScript";
import type { PageProps } from "keycloakify/login/pages/PageProps";
import { useIsPasswordRevealed } from "keycloakify/tools/useIsPasswordRevealed";
import { useEffect, type ReactNode } from "react";
import type { I18n } from "../i18n";
import type { KcContext } from "../KcContext";
import { Divider } from "../components/Divider";
import { LoginBox } from "../components/LoginBox";
import { LoginInput } from "../components/LoginInput";
import { ProviderIcon } from "../components/ProviderIcon";
import { useSubmitting } from "../lib/use-submitting";

/**
 * Вход (login.ftl) в два шага (плагин code-login, PasswordLoginForm, атрибут loginStep) — экраны 05, 06, 16 этапа 10:
 * - login — «Телефон или почта» (в проекте только одно — «Номер телефона» или «Почта», loginKinds) → «Продолжить»;
 *   ниже «или»: ключ доступа, Яндекс ID и Госуслуги;
 * - password — «Введите пароль»: что ввели в рамке с «Изменить», пароль сразу; ниже «или»: код на то, что ввели
 *   (номер — «Звонок», почта — «Код на почту») и ключ доступа (так и для незнакомого логина — по экрану не понять,
 *   есть ли аккаунт);
 * - methods — «Как войти?» (пароль выключен): способы карточками по цене (ключ доступа → код на почту → звонок),
 *   первый — основной.
 * Без loginStep — прежняя форма в один шаг (подтверждение пароля при привязке Госуслуг или Яндекс ID). Номер
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
    loginKinds,
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

  // Ключ доступа: скрытые формы (их заполняет скрипт Keycloak) и кнопка с webAuthnButtonId — где угодно на странице.
  const passkeyOn = !!enableWebAuthnConditionalUI;
  const passkeyForms = passkeyOn && (
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
    </>
  );
  const passkeyButton = (label: ReactNode) => (
    <button
      id={webAuthnButtonId}
      type="button"
      className={kcClsx("kcButtonClass", "kcButtonDefaultClass")}
    >
      {label}
    </button>
  );
  // Госуслуги и Яндекс ID — кнопками в ряд: значок и название.
  const providersNode = externalProviders.length > 0 && (
    <ul id="kc-social-providers" className="grid grid-cols-2 gap-2">
      {externalProviders.map((provider) => (
        <li key={provider.alias} className={externalProviders.length === 1 ? "col-span-2" : ""}>
          <a
            id={`social-${provider.alias}`}
            href={provider.loginUrl}
            className={kcClsx("kcButtonClass", "kcButtonDefaultClass")}
          >
            <span className="flex size-5 items-center justify-center [&_svg]:size-5">
              <ProviderIcon alias={provider.alias} name={provider.displayName} />
            </span>
            {provider.displayName}
          </a>
        </li>
      ))}
    </ul>
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
    <LoginBox
      login={loginDisplay}
      label={msgStr("platformLoginChange")}
      formId="platform-login-change-form"
    />
  );
  const changeLoginForm = loginDisplay !== undefined && (
    <form
      id="platform-login-change-form"
      action={url.loginAction}
      method="post"
      noValidate
      hidden
    />
  );
  // Ввели почту — код письмом, номер — звонок (на шаге пароля — на то, что ввели).
  const typedEmail = (loginDisplay ?? "").includes("@");
  const kinds = (loginKinds ?? "phone,email").split(",").filter(Boolean);
  const loginLabel =
    kinds.length === 1 && kinds[0] === "phone"
      ? msg("platformLoginPhoneLabel")
      : kinds.length === 1 && kinds[0] === "email"
        ? msg("platformLoginEmailLabel")
        : msg("usernameOrEmail");

  let header: ReactNode = msg("loginAccountTitle");
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
              {loginLabel}
            </label>
            <LoginInput
              className={kcClsx("kcInputClass")}
              defaultValue={login.username ?? ""}
              autoComplete={passkeyOn ? "username webauthn" : "username"}
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
        {(passkeyOn || providersNode) && (
          <>
            <Divider label={msgStr("platformLoginOr")} />
            <div className="grid gap-2">
              {passkeyOn && passkeyButton(msgStr("passkey-doAuthenticate"))}
              {providersNode}
            </div>
          </>
        )}
        {passkeyForms}
      </>
    );
  } else if (loginStep === "password") {
    header = msg("platformLoginPasswordTitle");
    const code = methods.find((method) => method === "email-code" || method === "phone-code");
    const passkey = passkeyOn && methods.includes("passkey");
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
          </div>
        </form>
        {changeLoginForm}
        {(code || passkey) && (
          <>
            <Divider label={msgStr("platformLoginOr")} />
            <div className={`grid gap-2 ${code && passkey ? "grid-cols-2" : ""}`}>
              {code && (
                <button
                  id={`platform-code-login-${code}`}
                  name="codeLogin"
                  value={code}
                  type="submit"
                  form="kc-form-login"
                  formNoValidate
                  disabled={submitting}
                  className={kcClsx("kcButtonClass", "kcButtonSecondaryClass")}
                >
                  {msgStr(typedEmail ? "platformLoginEmailCodeShort" : "platformLoginCall")}
                </button>
              )}
              {passkey && passkeyButton(msgStr("platformLoginPasskey"))}
            </div>
          </>
        )}
        {passkey && passkeyForms}
      </>
    );
  } else if (loginStep === "methods") {
    header = msg("platformLoginChooseMethod");
    const passkey = passkeyOn && methods.includes("passkey");
    const codes = methods.filter((method) => method === "email-code" || method === "phone-code");
    // По цене: ключ доступа, затем коды; первый — основной (залит).
    const card = (primary: boolean) =>
      `flex w-full items-center gap-3.5 border p-3.5 text-left transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:opacity-50 ${
        primary
          ? "border-accent bg-accent text-accent-foreground shadow-hard hover:bg-accent/90"
          : "border-primary bg-transparent hover:bg-muted"
      }`;
    const cardBody = (icon: string, title: ReactNode, hint: ReactNode, primary: boolean) => (
      <>
        <span
          aria-hidden
          className={`flex size-[30px] shrink-0 items-center justify-center border font-mono text-[11px] font-medium ${
            primary ? "border-accent-foreground" : "border-primary"
          }`}
        >
          {icon}
        </span>
        <span className="grid gap-0.5">
          <span className="text-[15px] font-semibold">{title}</span>
          <span className={`text-[13px] ${primary ? "opacity-80" : "text-muted-foreground"}`}>
            {hint}
          </span>
        </span>
      </>
    );
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
          {errorNode}
          {methods.length === 0 && (
            <p id="platform-login-no-methods" className="text-sm">
              {msg("platformLoginNoMethods")}
            </p>
          )}
        </form>
        {changeLoginForm}
        <div className="mt-4 grid gap-2.5">
          {passkey && (
            <button id={webAuthnButtonId} type="button" className={card(true)}>
              {cardBody("ID", msg("platformLoginPasskey"), msg("platformLoginPasskeyHint"), true)}
            </button>
          )}
          {codes.length > 0 && (
            <form
              id="platform-login-methods"
              className="grid gap-2.5"
              action={url.loginAction}
              method="post"
              noValidate
              onSubmit={markSubmitting}
            >
              {codes.map((method, index) => {
                const primary = !passkey && index === 0;
                const email = method === "email-code";
                return (
                  <button
                    key={method}
                    id={`platform-code-login-${method}`}
                    name="codeLogin"
                    value={method}
                    type="submit"
                    disabled={submitting}
                    className={card(primary)}
                  >
                    {cardBody(
                      email ? "@" : "☎",
                      msg(email ? "platformLoginEmailCodeShort" : "platformLoginCall"),
                      msg(
                        email ? "platformLoginEmailHintTo" : "platformLoginCallHintTo",
                        (email ? loginEmailHint : loginPhoneHint) ?? "",
                      ),
                      primary,
                    )}
                  </button>
                );
              })}
            </form>
          )}
          {methods.length === 0 && providersNode}
        </div>
        {passkey && passkeyForms}
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
      headerNode={header}
      displayInfo={realm.password && realm.registrationAllowed && !registrationDisabled}
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
                    autoComplete={passkeyOn ? "username webauthn" : "username"}
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
          {passkeyForms}
          {passkeyOn && (
            <div className="mt-2">{passkeyButton(msgStr("passkey-doAuthenticate"))}</div>
          )}
          {providersNode && <div className="mt-4">{providersNode}</div>}
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
