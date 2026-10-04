import type { ClassKey } from "keycloakify/login";

const button =
  "inline-flex h-11 w-full items-center justify-center gap-2 border px-4 text-sm font-medium transition-[background-color,transform,box-shadow] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:pointer-events-none disabled:opacity-50";

/**
 * Классы Keycloak → Tailwind: стандартные страницы (2FA, ключ доступа, сброс пароля…) выглядят как наши, без своей
 * вёрстки каждой. Свои страницы (вход, код) свёрстаны сами и берут те же классы.
 */
export const classes = {
  kcHtmlClass: "",
  kcBodyClass: "min-h-dvh",
  // Настройка 2FA (QR-код, шаги и форма) — шире, в две колонки (styles.css): иначе на ноутбуке не влезает в экран.
  kcLoginClass:
    "mx-auto flex min-h-dvh w-full max-w-md flex-col justify-center gap-6 px-4 py-10 lg:has-[#kc-totp-settings]:max-w-4xl lg:has-[#kc-totp-settings]:gap-4 lg:has-[#kc-totp-settings]:py-4",
  kcHeaderClass: "text-center",
  kcHeaderWrapperClass:
    "inline-flex items-center gap-2.5 font-mono text-[11px] font-medium tracking-[0.16em] text-muted-foreground uppercase before:size-3 before:bg-primary before:content-[''] has-[#kc-logo]:before:hidden",
  kcFormCardClass:
    "corner-mark border border-border bg-card p-6 sm:p-8 lg:has-[#kc-totp-settings]:py-6",
  kcFormHeaderClass: "mb-4 grid gap-2",
  kcFormClass: "grid gap-4",
  kcFormGroupClass: "grid gap-1.5",
  kcFormGroupErrorClass: "",
  kcLabelWrapperClass: "",
  kcLabelClass: "font-mono text-[11px] tracking-[0.1em] text-muted-foreground uppercase",
  kcInputWrapperClass: "grid gap-1",
  kcInputClass:
    "h-11 w-full border border-border bg-background px-3 text-[15px] placeholder:text-muted-foreground focus-visible:border-primary focus-visible:shadow-hard focus-visible:outline-none aria-invalid:border-dashed aria-invalid:border-primary aria-invalid:bg-hatch",
  kcTextareaClass:
    "min-h-20 w-full border border-border bg-background px-3 py-2 text-sm focus-visible:border-primary focus-visible:shadow-hard focus-visible:outline-none",
  kcInputErrorMessageClass:
    "inline-flex items-center gap-2 self-start bg-primary px-2 py-1 text-[13px] text-primary-foreground before:font-mono before:font-bold before:content-['!']",
  kcInputHelperTextBeforeClass: "text-xs text-muted-foreground",
  kcInputHelperTextAfterClass: "text-xs text-muted-foreground",
  kcInputGroup: "relative flex",
  kcFormPasswordVisibilityButtonClass:
    "absolute inset-y-0 right-0 flex items-center px-3 text-xs text-muted-foreground hover:text-foreground",
  kcFormPasswordVisibilityIconShow: "",
  kcFormPasswordVisibilityIconHide: "",
  kcFormOptionsClass: "text-sm",
  kcFormOptionsWrapperClass: "flex w-full items-center justify-between gap-2 text-sm",
  // Стоит рядом с kcFormGroupClass (ссылки и кнопки формы) — сетка, не строка: иначе кнопка сжимается.
  kcFormSettingClass: "gap-3 text-sm",
  kcCheckboxInputClass: "mr-2",
  kcFormButtonsClass: "grid gap-2",
  kcFormButtonsWrapperClass: "grid gap-2",
  kcButtonClass: button,
  kcButtonPrimaryClass:
    "border-primary bg-primary font-semibold text-primary-foreground shadow-hard hover:bg-primary/90 active:translate-x-1 active:translate-y-1 active:shadow-none",
  kcButtonDefaultClass: "border-primary bg-transparent hover:bg-muted",
  kcButtonSecondaryClass: "border-primary bg-transparent hover:bg-muted",
  kcButtonBlockClass: "w-full",
  kcButtonLargeClass: "",
  kcAlertClass: "mb-4 flex gap-2 border px-3 py-2 text-sm",
  kcAlertTitleClass: "",
  kcInfoAreaClass: "mt-4 text-center text-sm text-muted-foreground",
  kcInfoAreaWrapperClass: "",
  kcSignUpClass: "mt-4 text-center text-sm text-muted-foreground",
  kcLocaleMainClass: "flex justify-end",
  kcLocaleWrapperClass: "",
  kcLocaleDropDownClass: "",
  kcLocaleListClass: "",
  kcLocaleListItemClass: "",
  kcLocaleItemClass: "",
  kcContentWrapperClass: "",
  kcLoginOTPListClass: "grid gap-2",
  kcLoginOTPListInputClass: "peer sr-only",
  kcLoginOTPListItemHeaderClass:
    "flex cursor-pointer items-center gap-3 border border-border p-3 peer-checked:border-primary peer-checked:shadow-hard",
  kcLoginOTPListItemIconBodyClass: "",
  kcLoginOTPListItemIconClass: "",
  kcLoginOTPListItemTitleClass: "text-sm font-medium",
  kcSelectAuthListClass: "grid gap-2",
  kcSelectAuthListItemClass:
    "flex w-full cursor-pointer items-center gap-3 border border-border bg-background p-3 text-left hover:bg-muted",
  kcSelectAuthListItemHeadingClass: "text-sm font-medium",
  kcSelectAuthListItemDescriptionClass: "text-xs text-muted-foreground",
  kcSelectAuthListItemFillClass: "flex-1",
  kcSrOnlyClass: "sr-only",
  kcCheckInputClass: "mr-2",
  // Коды восстановления: предупреждение, список кодов, «Печать / Скачать / Копировать».
  kcRecoveryCodesWarning: "my-3 border border-dashed border-primary bg-hatch p-3 text-sm",
  kcRecoveryCodesList:
    "my-3 grid grid-cols-2 gap-2 border border-border bg-background p-3 font-mono text-sm",
  kcRecoveryCodesActions: "mb-3 flex flex-wrap gap-2",
} satisfies { [key in ClassKey]?: string };
