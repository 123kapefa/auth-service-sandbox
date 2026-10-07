import { kcSanitize } from "keycloakify/lib/kcSanitize";
import { getKcClsx } from "keycloakify/login/lib/kcClsx";
import type { PageProps } from "keycloakify/login/pages/PageProps";
import { useState } from "react";
import type { I18n } from "../i18n";
import type { KcContext } from "../KcContext";
import { formatLogin, nextLogin } from "../lib/phone";
import { useSubmitting } from "../lib/use-submitting";
import CodeLogin from "./CodeLogin";

type Props = PageProps<Extract<KcContext, { pageId: "platform-update-phone.ftl" }>, I18n>;

/**
 * Смена номера (плагин code-login, действие platform-update-phone), экран 13 этапа 10: новый номер → код на него (тот же
 * экран, что у входа по коду: звонок, затем SMS; «Изменить» у номера — другой номер) → номер изменён. «Отмена» — назад
 * в кабинет без изменений.
 */
export default function UpdatePhone(props: Props) {
  const { kcContext, i18n } = props;
  const { msg, msgStr } = i18n;

  if (kcContext.phoneStep === "code") {
    const { url } = kcContext;
    return (
      <CodeLogin
        {...(props as unknown as PageProps<
          Extract<KcContext, { pageId: "platform-code-login.ftl" }>,
          I18n
        >)}
        title={msg("platformPhoneTitle")}
        submitLabel={msgStr("platformPhoneConfirm")}
        changeName="changePhone"
        footer={<CancelLink action={url.loginAction} label={msgStr("doCancel")} />}
      />
    );
  }
  return <PhoneStep {...props} />;
}

function PhoneStep(props: Props) {
  const { kcContext, i18n, doUseDefaultCss, Template, classes } = props;
  const { kcClsx } = getKcClsx({ doUseDefaultCss, classes });
  const { url, phoneCurrent, phoneNextAllowed } = kcContext;
  const { msg, msgStr } = i18n;
  const { submitting, markSubmitting } = useSubmitting();
  const [value, setValue] = useState("");
  const blocked = phoneNextAllowed !== undefined;
  const error = kcContext.message?.type === "error" ? kcContext.message.summary : undefined;

  return (
    <Template
      kcContext={kcContext}
      i18n={i18n}
      doUseDefaultCss={doUseDefaultCss}
      classes={classes}
      displayMessage={false}
      headerNode={msg("platformPhoneNewTitle")}
    >
      <p id="platform-phone-current" className="mb-4 text-sm leading-relaxed">
        {phoneCurrent && (
          <>
            {msgStr("platformPhoneCurrentLabel")}{" "}
            <span className="font-mono">{formatLogin(phoneCurrent)}</span>.{" "}
          </>
        )}
        {msg("platformPhoneWhy")}
      </p>
      <form
        id="platform-phone-form"
        className={kcClsx("kcFormClass")}
        action={url.loginAction}
        method="post"
        noValidate
        onSubmit={markSubmitting}
      >
        {!blocked && (
          <div className={kcClsx("kcFormGroupClass")}>
            <label htmlFor="phone" className={kcClsx("kcLabelClass")}>
              {msg("platformPhoneNewLabel")}
            </label>
            <input
              id="phone"
              name="phone"
              type="tel"
              className={kcClsx("kcInputClass")}
              value={value}
              onChange={(event) => setValue(nextLogin(value, event.target.value))}
              onBlur={() => setValue(formatLogin(value))}
              autoComplete="tel"
              autoFocus
              aria-invalid={error ? true : undefined}
              aria-describedby={error ? "input-error" : undefined}
            />
          </div>
        )}
        {error && (
          <span
            id="input-error"
            role="alert"
            className={kcClsx("kcInputErrorMessageClass")}
            dangerouslySetInnerHTML={{ __html: kcSanitize(error) }}
          />
        )}
        <div className={kcClsx("kcFormButtonsClass")}>
          {!blocked && (
            <button
              id="platform-phone-send"
              name="send"
              type="submit"
              disabled={submitting}
              className={kcClsx("kcButtonClass", "kcButtonPrimaryClass")}
            >
              {msgStr("platformPhoneSend")}
            </button>
          )}
        </div>
      </form>
      {!blocked && (
        <p className="mt-4 text-[13px] leading-relaxed text-muted-foreground">
          {msg("platformPhoneNotice")}
        </p>
      )}
      <CancelLink action={url.loginAction} label={msgStr("doCancel")} />
    </Template>
  );
}

/** «Отмена» ссылкой внизу: назад в кабинет без изменений (cancel-aia — отмена действия Keycloak). */
function CancelLink({ action, label }: { action: string; label: string }) {
  return (
    <form action={action} method="post" noValidate className="mt-3 text-center text-sm">
      <button
        id="platform-phone-cancel"
        name="cancel-aia"
        value="true"
        type="submit"
        className="underline underline-offset-4 hover:text-muted-foreground"
      >
        {label}
      </button>
    </form>
  );
}
