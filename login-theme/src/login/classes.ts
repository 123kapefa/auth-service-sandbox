import type { ClassKey } from "keycloakify/login";

const button =
  "inline-flex h-10 w-full items-center justify-center gap-2 rounded-md px-4 text-sm font-medium transition-colors focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none disabled:pointer-events-none disabled:opacity-50";

/**
 * Классы Keycloak → Tailwind: стандартные страницы (2FA, ключ доступа, сброс пароля…) выглядят как наши, без своей
 * вёрстки каждой. Свои страницы (вход, код) свёрстаны сами и берут те же классы.
 */
export const classes = {
  kcHtmlClass: "",
  kcBodyClass: "min-h-dvh",
  kcLoginClass: "mx-auto flex min-h-dvh w-full max-w-md flex-col justify-center gap-6 px-4 py-10",
  kcHeaderClass: "text-center",
  kcHeaderWrapperClass: "text-sm font-semibold tracking-wide text-muted-foreground uppercase",
  kcFormCardClass: "rounded-xl border border-border bg-card p-6 shadow-sm",
  kcFormHeaderClass: "mb-4 grid gap-2",
  kcFormClass: "grid gap-4",
  kcFormGroupClass: "grid gap-1.5",
  kcFormGroupErrorClass: "",
  kcLabelWrapperClass: "",
  kcLabelClass: "text-sm font-medium",
  kcInputWrapperClass: "grid gap-1",
  kcInputClass:
    "h-10 w-full rounded-md border border-border bg-card px-3 text-sm placeholder:text-muted-foreground focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none aria-invalid:border-destructive",
  kcTextareaClass:
    "min-h-20 w-full rounded-md border border-border bg-card px-3 py-2 text-sm focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none",
  kcInputErrorMessageClass: "text-sm text-destructive",
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
  kcButtonPrimaryClass: "bg-primary text-primary-foreground hover:bg-primary/90",
  kcButtonDefaultClass: "border border-border bg-card hover:bg-muted",
  kcButtonSecondaryClass: "border border-border bg-card hover:bg-muted",
  kcButtonBlockClass: "w-full",
  kcButtonLargeClass: "",
  kcAlertClass: "mb-4 flex gap-2 rounded-md border px-3 py-2 text-sm",
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
    "flex cursor-pointer items-center gap-3 rounded-md border border-border p-3 peer-checked:border-primary",
  kcLoginOTPListItemIconBodyClass: "",
  kcLoginOTPListItemIconClass: "",
  kcLoginOTPListItemTitleClass: "text-sm font-medium",
  kcSelectAuthListClass: "grid gap-2",
  kcSelectAuthListItemClass:
    "flex w-full cursor-pointer items-center gap-3 rounded-md border border-border p-3 text-left hover:bg-muted",
  kcSelectAuthListItemHeadingClass: "text-sm font-medium",
  kcSelectAuthListItemDescriptionClass: "text-xs text-muted-foreground",
  kcSelectAuthListItemFillClass: "flex-1",
  kcSrOnlyClass: "sr-only",
  kcCheckInputClass: "mr-2",
  // Коды восстановления: предупреждение, список кодов, «Печать / Скачать / Копировать».
  kcRecoveryCodesWarning: "my-3 rounded-md border border-warning/50 p-3 text-sm",
  kcRecoveryCodesList: "my-3 grid grid-cols-2 gap-2 rounded-md bg-muted p-3 font-mono text-sm",
  kcRecoveryCodesActions: "mb-3 flex flex-wrap gap-2",
} satisfies { [key in ClassKey]?: string };
