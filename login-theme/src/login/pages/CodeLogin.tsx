import { getKcClsx } from "keycloakify/login/lib/kcClsx";
import type { PageProps } from "keycloakify/login/pages/PageProps";
import { useRef, useState } from "react";
import type { I18n } from "../i18n";
import type { KcContext } from "../KcContext";
import { useCountdown } from "../lib/use-countdown";
import { useSubmitting } from "../lib/use-submitting";

/**
 * Вход по коду (плагин code-login, sandbox-code-login.ftl): куда ушёл код, поле кода (только цифры; набрали все —
 * форма уходит сама), «Отправить код ещё раз» — когда истечёт пауза (её же проверяет сервер), назад к паролю.
 * Ошибки (неверный код, блокировка, код истёк, рано) — сообщением Keycloak в каркасе.
 */
export default function CodeLogin(
  props: PageProps<Extract<KcContext, { pageId: "sandbox-code-login.ftl" }>, I18n>,
) {
  const { kcContext, i18n, doUseDefaultCss, Template, classes } = props;
  const { kcClsx } = getKcClsx({ doUseDefaultCss, classes });
  const { url, codeChannel, codeTarget, codeResendAfter, codeLength } = kcContext;
  const { msg, msgStr } = i18n;
  const byEmail = codeChannel === "EMAIL";
  const length = Number(codeLength) || 6;
  const resendIn = useCountdown(Number(codeResendAfter) || 0);
  const [code, setCode] = useState("");
  const [focused, setFocused] = useState(false);
  const { submitting, markSubmitting, setSubmitting } = useSubmitting();
  const form = useRef<HTMLFormElement>(null);

  const type = (value: string) => {
    const digits = value.replace(/\D/g, "").slice(0, length);
    setCode(digits);
    if (digits.length === length && !submitting) {
      setSubmitting(true);
      // После отрисовки: в форму должно попасть уже полное значение.
      setTimeout(() => form.current?.requestSubmit(), 0);
    }
  };

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
        ref={form}
        id="sandbox-code-form"
        className={kcClsx("kcFormClass")}
        action={url.loginAction}
        method="post"
        noValidate
        onSubmit={markSubmitting}
      >
        <div className={kcClsx("kcFormGroupClass")}>
          <label htmlFor="code" className={kcClsx("kcLabelClass")}>
            {msg(byEmail ? "sandboxCodeLabelEmail" : "sandboxCodeLabelSms")}
          </label>
          {/*
            Ячейки по цифре — только вид; поле одно и прозрачное поверх них: автоподстановка кода из SMS, вставка
            и ввод работают как у обычного поля.
          */}
          <div className="relative">
            <div
              aria-hidden
              className="grid gap-2"
              style={{ gridTemplateColumns: `repeat(${length}, 1fr)` }}
            >
              {Array.from({ length }, (_, i) => (
                <div
                  key={i}
                  className={`flex h-12 items-center justify-center rounded-md border bg-card font-mono text-xl font-semibold transition-colors ${
                    focused && i === Math.min(code.length, length - 1)
                      ? "border-primary ring-2 ring-ring/40"
                      : "border-border"
                  }`}
                >
                  {code[i] ??
                    (focused && i === code.length ? (
                      <span className="h-6 w-px animate-pulse bg-foreground" />
                    ) : null)}
                </div>
              ))}
            </div>
            <input
              id="code"
              name="code"
              value={code}
              onChange={(event) => type(event.target.value)}
              onFocus={() => setFocused(true)}
              onBlur={() => setFocused(false)}
              className="absolute inset-0 h-full w-full cursor-text bg-transparent text-transparent caret-transparent outline-none selection:bg-transparent"
              autoComplete="one-time-code"
              inputMode="numeric"
              autoFocus
            />
          </div>
        </div>
        <div className={kcClsx("kcFormButtonsClass")}>
          <button
            id="kc-login"
            name="login"
            type="submit"
            disabled={submitting || code.length < length}
            className={kcClsx("kcButtonClass", "kcButtonPrimaryClass")}
          >
            {msgStr("doLogIn")}
          </button>
        </div>
      </form>
      {/* Отдельная форма: «ещё раз» не отправляет недописанный код. */}
      <form action={url.loginAction} method="post" className="mt-2" onSubmit={markSubmitting}>
        <button
          id="sandbox-code-resend"
          name="resend"
          type="submit"
          disabled={resendIn > 0 || submitting}
          className={kcClsx("kcButtonClass", "kcButtonSecondaryClass")}
        >
          {msgStr("sandboxCodeResend")}
        </button>
      </form>
      {resendIn > 0 && (
        <p
          id="sandbox-code-resend-after"
          aria-live="polite"
          className="mt-3 text-sm text-muted-foreground"
        >
          {msg("sandboxCodeResendIn", String(resendIn))}
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
