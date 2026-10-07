import { getKcClsx } from "keycloakify/login/lib/kcClsx";
import type { PageProps } from "keycloakify/login/pages/PageProps";
import { useRef, useState, type ReactNode } from "react";
import type { I18n } from "../i18n";
import type { KcContext } from "../KcContext";
import { Captcha } from "../components/Captcha";
import { CodeCells } from "../components/CodeCells";
import { LoginBox } from "../components/LoginBox";
import { formatLogin } from "../lib/phone";
import { useCountdown } from "../lib/use-countdown";
import { useSubmitting } from "../lib/use-submitting";

type Props = PageProps<Extract<KcContext, { pageId: "platform-code-login.ftl" }>, I18n> & {
  /** Свой заголовок (смена номера) — вместо «Вам звонят», «Код из SMS»… */
  title?: ReactNode;
  submitLabel?: string;
  /** Ссылка внизу — вместо «Выбрать другой способ». */
  footer?: ReactNode;
  /** «Изменить» у адреса: кнопка с этим именем (смена номера — changePhone) вместо «начать вход заново». */
  changeName?: string;
};

/** Секунды → 0:42. */
function clock(seconds: number): string {
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

/**
 * Вход по коду (плагин code-login, platform-code-login.ftl) — экраны 09–11, 17, 18 этапа 10:
 * - «Вам звонят» — код — последние 4 цифры номера, с которого звонят; пока идёт пауза — «Позвонить ещё раз или
 *   прислать SMS — через 0:42», после неё — «Звонок не пришёл?» с «Прислать SMS» и «Позвонить ещё раз»;
 * - «Код из SMS», «Код из письма» — после паузы «Отправить ещё раз»;
 * - «Проверка» (captchaStep) — код не ушёл без капчи: капча и та же отправка.
 * Куда ушёл код — в рамке с «Изменить» (начать вход заново). Набрали все цифры — форма уходит сама. Ошибки (неверный
 * код, блокировка, код истёк, рано) — сообщением Keycloak в каркасе. Тот же экран — у смены номера (UpdatePhone).
 */
export default function CodeLogin(props: Props) {
  if (props.kcContext.captchaStep === "true" && props.kcContext.captchaSiteKey) {
    return <CaptchaStep {...props} />;
  }
  return <CodeStep {...props} />;
}

function CodeStep(props: Props) {
  const {
    kcContext,
    i18n,
    doUseDefaultCss,
    Template,
    classes,
    title,
    submitLabel,
    footer,
    changeName,
  } = props;
  const { kcClsx } = getKcClsx({ doUseDefaultCss, classes });
  const {
    url,
    codeChannel,
    codeDelivery,
    codeSmsAvailable,
    codeTarget,
    codeResendAfter,
    codeLength,
  } = kcContext;
  const { msg, msgStr } = i18n;
  const delivery = codeDelivery ?? (codeChannel === "EMAIL" ? "EMAIL" : "SMS");
  const call = delivery === "CALL";
  const sms = codeSmsAvailable === "true";
  const length = Number(codeLength) || 6;
  const resendIn = useCountdown(Number(codeResendAfter) || 0);
  // Звонка не было за паузу — экран «Звонок не пришёл?»: SMS или звонок ещё раз.
  const noCall = call && resendIn === 0;
  const [code, setCode] = useState("");
  const { submitting, markSubmitting, setSubmitting } = useSubmitting();
  const form = useRef<HTMLFormElement>(null);
  const wrong = kcContext.message?.type === "error";

  const type = (digits: string) => {
    setCode(digits);
    if (digits.length === length && !submitting) {
      setSubmitting(true);
      // После отрисовки: в форму должно попасть уже полное значение.
      setTimeout(() => form.current?.requestSubmit(), 0);
    }
  };

  const heading =
    title ??
    msg(
      noCall
        ? "platformCodeNoCallTitle"
        : call
          ? "platformCodeTitleCall"
          : delivery === "EMAIL"
            ? "platformCodeTitleEmail"
            : "platformCodeTitleSms",
    );

  // Повторная отправка: своя форма — не отправляет недописанный код.
  const sendButton = (name: "resend" | "sendSms", primary: boolean) => (
    <form action={url.loginAction} method="post" onSubmit={markSubmitting}>
      <button
        id={name === "resend" ? "platform-code-resend" : "platform-code-send-sms"}
        name={name}
        type="submit"
        disabled={submitting}
        className={kcClsx(
          "kcButtonClass",
          primary ? "kcButtonPrimaryClass" : "kcButtonSecondaryClass",
        )}
      >
        {msgStr(
          name === "sendSms"
            ? "platformCodeSendSms"
            : call
              ? "platformCodeCallAgain"
              : "platformCodeResend",
        )}
      </button>
    </form>
  );

  return (
    <Template
      kcContext={kcContext}
      i18n={i18n}
      doUseDefaultCss={doUseDefaultCss}
      classes={classes}
      headerNode={heading}
    >
      <div className="grid gap-4">
        <LoginBox
          login={codeTarget}
          label={msgStr("platformLoginChange")}
          href={changeName ? undefined : url.loginRestartFlowUrl}
          formId={changeName ? "platform-code-change-form" : undefined}
          name={changeName}
        />
        <p id="platform-code-target" className="text-sm leading-relaxed">
          {noCall
            ? msg(sms ? "platformCodeNoCallTextSms" : "platformCodeNoCallText")
            : call
              ? msg("platformCodeCallHint")
              : delivery === "EMAIL"
                ? msg("platformCodeEmailHint", String(length), codeTarget)
                : msg("platformCodeSmsHint", String(length), formatLogin(codeTarget))}
        </p>
        {call && !noCall && (
          <p aria-hidden className="flex items-center gap-1.5 font-mono text-lg font-medium">
            <span className="text-muted-foreground">••• •••-</span>
            <span className="border border-dashed border-primary px-1.5">XX XX</span>
          </p>
        )}
        <form
          ref={form}
          id="platform-code-form"
          className={kcClsx("kcFormClass")}
          action={url.loginAction}
          method="post"
          noValidate
          onSubmit={markSubmitting}
        >
          <label htmlFor="code" className="sr-only">
            {msg(
              call
                ? "platformCodeLabelCall"
                : delivery === "EMAIL"
                  ? "platformCodeLabelEmail"
                  : "platformCodeLabelSms",
            )}
          </label>
          <CodeCells
            id="code"
            name="code"
            length={length}
            value={code}
            onChange={type}
            invalid={wrong && code.length === 0}
          />
          {!noCall && (
            <div className={kcClsx("kcFormButtonsClass")}>
              <button
                id="kc-login"
                name="login"
                type="submit"
                disabled={submitting || code.length < length}
                className={kcClsx("kcButtonClass", "kcButtonPrimaryClass")}
              >
                {submitLabel ?? msgStr("doLogIn")}
              </button>
            </div>
          )}
        </form>
        {noCall && (
          <div className="grid gap-2">
            {sms && sendButton("sendSms", true)}
            {sendButton("resend", !sms)}
          </div>
        )}
        {resendIn > 0 ? (
          <p
            id="platform-code-resend-after"
            aria-live="polite"
            className="text-center font-mono text-[11px] tracking-[0.04em] text-muted-foreground uppercase"
          >
            {msg(
              call
                ? sms
                  ? "platformCodeCallOrSmsIn"
                  : "platformCodeCallAgainIn"
                : "platformCodeResendIn",
              clock(resendIn),
            )}
          </p>
        ) : (
          !call && (
            <form
              action={url.loginAction}
              method="post"
              className="text-center"
              onSubmit={markSubmitting}
            >
              <button
                id="platform-code-resend"
                name="resend"
                type="submit"
                disabled={submitting}
                className="font-mono text-[11px] tracking-[0.04em] uppercase underline underline-offset-4 hover:text-muted-foreground"
              >
                {msgStr("platformCodeResendShort")}
              </button>
            </form>
          )
        )}
      </div>
      {footer ?? (
        <p className="mt-4 text-center text-sm">
          <a
            id="platform-code-back"
            href={url.loginRestartFlowUrl}
            className="underline underline-offset-4 hover:text-muted-foreground"
          >
            {msg("platformCodeOtherMethod")}
          </a>
        </p>
      )}
      {changeName && (
        <form
          id="platform-code-change-form"
          action={url.loginAction}
          method="post"
          noValidate
          hidden
        />
      )}
    </Template>
  );
}

/** «Проверка»: код не ушёл без капчи. Капча — в форме отправки, ответ уходит полем smart-token. */
function CaptchaStep(props: Props) {
  const { kcContext, i18n, doUseDefaultCss, Template, classes, title, footer, changeName } = props;
  const { kcClsx } = getKcClsx({ doUseDefaultCss, classes });
  const { url, codeChannel, codeDelivery, codeTarget, captchaSiteKey } = kcContext;
  const { msg, msgStr, currentLanguage } = i18n;
  const delivery = codeDelivery ?? (codeChannel === "EMAIL" ? "EMAIL" : "SMS");
  const [token, setToken] = useState("");
  const { submitting, markSubmitting } = useSubmitting();

  return (
    <Template
      kcContext={kcContext}
      i18n={i18n}
      doUseDefaultCss={doUseDefaultCss}
      classes={classes}
      displayMessage={false}
      headerNode={title ?? msg("platformCaptchaTitle")}
    >
      <form
        id="platform-captcha-form"
        className={kcClsx("kcFormClass")}
        action={url.loginAction}
        method="post"
        noValidate
        onSubmit={markSubmitting}
      >
        <LoginBox
          login={codeTarget}
          label={msgStr("platformLoginChange")}
          href={changeName ? undefined : url.loginRestartFlowUrl}
          formId={changeName ? "platform-code-change-form" : undefined}
          name={changeName}
        />
        <p className="text-sm leading-relaxed">
          {msg(delivery === "CALL" ? "platformCaptchaTextCall" : "platformCaptchaTextSend")}
        </p>
        <Captcha
          siteKey={captchaSiteKey ?? ""}
          language={currentLanguage.languageTag}
          label={msgStr("platformCaptchaLabel")}
          stubNote={msgStr("platformCaptchaStub")}
          onToken={setToken}
        />
        <div className={kcClsx("kcFormButtonsClass")}>
          <button
            id="platform-captcha-send"
            name={delivery === "SMS" ? "sendSms" : "resend"}
            type="submit"
            disabled={submitting || token === ""}
            className={kcClsx("kcButtonClass", "kcButtonPrimaryClass")}
          >
            {msgStr(delivery === "CALL" ? "platformCaptchaCall" : "platformCaptchaSend")}
          </button>
        </div>
      </form>
      {footer ?? (
        <p className="mt-4 text-center text-sm">
          <a
            id="platform-code-back"
            href={url.loginRestartFlowUrl}
            className="underline underline-offset-4 hover:text-muted-foreground"
          >
            {msg("platformCaptchaOther")}
          </a>
        </p>
      )}
      {changeName && (
        <form
          id="platform-code-change-form"
          action={url.loginAction}
          method="post"
          noValidate
          hidden
        />
      )}
    </Template>
  );
}
