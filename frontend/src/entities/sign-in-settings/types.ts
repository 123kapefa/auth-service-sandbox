/** Канал кода на телефон: звонок-сброс (первым) и SMS (запасной). */
export type PhoneChannel = "call" | "sms";

/** Коды подтверждения проекта (GET /auth/admin/codes/settings). Деньги — в копейках. */
export interface CodeSettings {
  phoneChannels: PhoneChannel[];
  /** Коды стран без «+»: 7, 375. */
  countries: string[];
  /** Дневной бюджет на звонки и SMS; 0 — без бюджета. */
  dailyBudget: number;
  targetPerHour: number;
  targetPerDay: number;
  ipPerHour: number;
  rangePerHour: number;
  captchaAfter: number;
  /** Дальше — настройки сервера: только показать. */
  rangeDigits: number;
  smsCost: number;
  callCost: number;
  budgetAlertPercent: number;
  zone: string;
}

/** Что меняет админ (PUT /auth/admin/codes/settings) — целиком. */
export type CodeSettingsChanges = Pick<
  CodeSettings,
  | "phoneChannels"
  | "countries"
  | "dailyBudget"
  | "targetPerHour"
  | "targetPerDay"
  | "ipPerHour"
  | "rangePerHour"
  | "captchaAfter"
>;

export interface CodeChannelUsage {
  sent: number;
  cost: number;
}

/** Расход кодов за сегодня (GET /auth/admin/codes/usage). */
export interface CodeUsage {
  date: string;
  zone: string;
  budget: { spent: number; limit: number; alertPercent: number; exhausted: boolean };
  call: CodeChannelUsage;
  sms: CodeChannelUsage;
  email: CodeChannelUsage;
  sent: number;
  verified: number;
  /** null — сегодня ничего не отправлено. */
  verifiedPercent: number | null;
}

/** Способ входа проекта. */
export type ProjectMethod = "phone" | "email" | "password" | "passkey" | "yandex" | "esia";

export const PROJECT_METHODS: ProjectMethod[] = [
  "phone",
  "email",
  "password",
  "passkey",
  "yandex",
  "esia",
];

/** Способы с переходным периодом (остальные выключаются сразу). */
export const TRANSITIONAL_METHODS: ProjectMethod[] = ["phone", "email", "password"];

/** Способы регистрации: хотя бы один включён всегда. */
export const REGISTRATION_METHODS: ProjectMethod[] = ["phone", "email", "yandex", "esia"];

export interface ProjectMethodState {
  status: "on" | "off" | "transition";
  /** Конец переходного периода, yyyy-MM-dd (включительно). */
  until?: string | null;
  since?: string | null;
  /** Только у yandex и esia: настроен ли в Keycloak. */
  configured?: boolean | null;
}

/** Способы входа проекта (GET /auth/admin/sign-in/methods). */
export interface ProjectSignIn {
  methods: Record<ProjectMethod, ProjectMethodState>;
  twoFactor: "optional" | "required";
}

/** Сколько людей входит способом и сколько — только им. */
export interface MethodUsers {
  method: ProjectMethod;
  users: number;
  only: number;
}
