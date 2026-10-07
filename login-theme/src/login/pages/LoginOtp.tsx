import { kcSanitize } from "keycloakify/lib/kcSanitize";
import { getKcClsx } from "keycloakify/login/lib/kcClsx";
import type { PageProps } from "keycloakify/login/pages/PageProps";
import { useRef, useState } from "react";
import type { I18n } from "../i18n";
import type { KcContext } from "../KcContext";
import { CodeCells } from "../components/CodeCells";
import { useSubmitting } from "../lib/use-submitting";

const OTP_LENGTH = 6;

/**
 * Код из приложения-аутентификатора (login-otp.ftl, 2FA) — экран 19 этапа 10: шесть ячеек, как у кода входа (набрали
 * все — форма уходит сама), «Подтвердить». Несколько приложений — выбор карточками. Коды восстановления — «Другой
 * способ входа» каркаса.
 */
export default function LoginOtp(
  props: PageProps<Extract<KcContext, { pageId: "login-otp.ftl" }>, I18n>,
) {
  const { kcContext, i18n, doUseDefaultCss, Template, classes } = props;
  const { kcClsx } = getKcClsx({ doUseDefaultCss, classes });
  const { otpLogin, url, messagesPerField } = kcContext;
  const { msg, msgStr } = i18n;
  const { submitting, markSubmitting, setSubmitting } = useSubmitting();
  const [code, setCode] = useState("");
  const form = useRef<HTMLFormElement>(null);
  const invalid = messagesPerField.existsError("totp");

  const type = (digits: string) => {
    setCode(digits);
    if (digits.length === OTP_LENGTH && !submitting) {
      setSubmitting(true);
      setTimeout(() => form.current?.requestSubmit(), 0);
    }
  };

  return (
    <Template
      kcContext={kcContext}
      i18n={i18n}
      doUseDefaultCss={doUseDefaultCss}
      classes={classes}
      displayMessage={!invalid}
      headerNode={msg("platformOtpTitle")}
    >
      <p className="mb-4 text-sm leading-relaxed">{msg("platformOtpText")}</p>
      <form
        ref={form}
        id="kc-otp-login-form"
        className={kcClsx("kcFormClass")}
        action={url.loginAction}
        method="post"
        onSubmit={markSubmitting}
      >
        {otpLogin.userOtpCredentials.length > 1 && (
          <div className={kcClsx("kcLoginOTPListClass")}>
            {otpLogin.userOtpCredentials.map((credential, index) => (
              <label key={credential.id} className="block">
                <input
                  id={`kc-otp-credential-${index}`}
                  className={kcClsx("kcLoginOTPListInputClass")}
                  type="radio"
                  name="selectedCredentialId"
                  value={credential.id}
                  defaultChecked={credential.id === otpLogin.selectedCredentialId}
                />
                <span className={kcClsx("kcLoginOTPListItemHeaderClass")}>
                  <span className={kcClsx("kcLoginOTPListItemTitleClass")}>
                    {credential.userLabel}
                  </span>
                </span>
              </label>
            ))}
          </div>
        )}
        <div className={kcClsx("kcFormGroupClass")}>
          <label htmlFor="otp" className="sr-only">
            {msg("loginOtpOneTime")}
          </label>
          <CodeCells
            id="otp"
            name="otp"
            length={OTP_LENGTH}
            value={code}
            onChange={type}
            invalid={invalid}
          />
          {invalid && (
            <span
              id="input-error-otp-code"
              className={kcClsx("kcInputErrorMessageClass")}
              aria-live="polite"
              dangerouslySetInnerHTML={{ __html: kcSanitize(messagesPerField.get("totp")) }}
            />
          )}
        </div>
        <div className={kcClsx("kcFormButtonsClass")}>
          <button
            id="kc-login"
            name="login"
            type="submit"
            disabled={submitting || code.length < OTP_LENGTH}
            className={kcClsx("kcButtonClass", "kcButtonPrimaryClass")}
          >
            {msgStr("platformOtpConfirm")}
          </button>
        </div>
      </form>
    </Template>
  );
}
