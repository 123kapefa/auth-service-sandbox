import type { ExtendKcContext } from "keycloakify/login";
import type { KcEnvName, ThemeName } from "../kc.gen";

export type KcContextExtension = {
  themeName: ThemeName;
  properties: Record<KcEnvName, string> & {};
};

/**
 * Свои страницы плагина (code-login): ключ — имя шаблона из createForm(...), поля — атрибуты, которые ставит
 * аутентификатор (form.setAttribute). Keycloakify по ключу собирает .ftl в тему.
 */
type CodeScreen = {
  /** SMS или EMAIL — куда ушёл код. */
  codeChannel: "SMS" | "EMAIL";
  /** Как ушёл код: CALL — звонок-сброс (код — последние 4 цифры номера), SMS, EMAIL. */
  codeDelivery: "CALL" | "SMS" | "EMAIL";
  /** "true" — на экране звонка есть «Прислать SMS» (у проекта SMS включены). */
  codeSmsAvailable: string;
  /** Куда отправлен код — как показать пользователю. */
  codeTarget: string;
  /** Через сколько секунд можно запросить новый код ("0" — уже можно). */
  codeResendAfter: string;
  /** Сколько цифр в коде (политика плагина). */
  codeLength: string;
  /** "true" — следующий код уйдёт только с капчей (поле smart-token в форме). Экран капчи — 10.8. */
  captchaRequired?: string;
  /** Ключ клиента Яндекс SmartCaptcha (нет — капча не настроена). */
  captchaSiteKey?: string;
};

export type KcContextExtensionPerPage = {
  /** Смена номера (действие platform-update-phone): шаг phone — новый номер, code — код на него (как у входа). */
  "platform-update-phone.ftl": Partial<CodeScreen> & {
    phoneStep: "phone" | "code";
    /** Нынешний номер с «+» (нет — номера не было). */
    phoneCurrent?: string;
    /** Меняли меньше суток назад: когда снова можно (ISO). */
    phoneNextAllowed?: string;
  };
  /** Вход в два шага (PasswordLoginForm). Без loginStep — прежняя форма в один шаг (подтверждение пароля при привязке). */
  "login.ftl": {
    /** login — телефон или почта; password — пароль сразу; methods — список способов (пароль выключен). */
    loginStep?: "login" | "password" | "methods";
    /** Способы шага 2 через запятую по цене: passkey, email-code, phone-code. */
    loginMethods?: string;
    /** Что ввели на шаге 1 — как показать (номер с «+»). */
    loginDisplay?: string;
    /** Почта и номер из аккаунта, скрытые частично (шаг «способы»). */
    loginEmailHint?: string;
    loginPhoneHint?: string;
    /** Единственный способ — открыть сам (passkey). */
    loginAutoStart?: string;
  };
  "platform-code-login.ftl": CodeScreen;
  /** Регистрация ключа доступа; platformPasskeyOffer "true" — это предложение после входа по коду (10.6). */
  "webauthn-register.ftl": PasskeyOffer;
  "webauthn-error.ftl": PasskeyOffer;
};

type PasskeyOffer = {
  /** "true" — экран «Входите без кода» (действие platform-passkey-offer) с «Не сейчас». */
  platformPasskeyOffer?: string;
};

export type KcContext = ExtendKcContext<KcContextExtension, KcContextExtensionPerPage>;
