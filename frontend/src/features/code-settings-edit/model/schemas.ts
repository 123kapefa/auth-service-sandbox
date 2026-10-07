import { z } from "zod";
import {
  type CodeSettings,
  type CodeSettingsChanges,
  type PhoneChannel,
} from "@/entities/sign-in-settings";

/** Пределы — как у сервиса (CodeSettingsRules): не сохранится всё равно, но подскажем сразу. */
export const CODE_LIMITS = {
  countries: 20,
  budgetRubles: 10_000_000,
  targetPerHour: 100,
  targetPerDay: 1000,
  ipPerHour: 10_000,
  rangePerHour: 100_000,
  captchaAfter: 100,
} as const;

const whole = (min: number, max: number) =>
  z
    .string()
    .trim()
    .regex(/^\d+$/, "Целое число")
    .refine((value) => Number(value) >= min && Number(value) <= max, `От ${min} до ${max}`);

/** Поля формы — строки (как в полях ввода); числа и копейки — в fromForm. */
export const codeSettingsSchema = z
  .object({
    call: z.boolean(),
    sms: z.boolean(),
    /** «+7, +375» или «7 375». */
    countries: z.string(),
    /** Рубли, можно с пробелами и копейками через запятую; 0 — без бюджета. */
    budget: z.string(),
    targetPerHour: whole(1, CODE_LIMITS.targetPerHour),
    targetPerDay: whole(1, CODE_LIMITS.targetPerDay),
    ipPerHour: whole(1, CODE_LIMITS.ipPerHour),
    rangePerHour: whole(1, CODE_LIMITS.rangePerHour),
    captchaAfter: whole(0, CODE_LIMITS.captchaAfter),
  })
  .superRefine((form, ctx) => {
    if (!form.call && !form.sms) {
      ctx.addIssue({ code: "custom", path: ["sms"], message: "Оставьте звонки или SMS" });
    }

    const countries = parseCountries(form.countries);
    if (countries === null) {
      ctx.addIssue({ code: "custom", path: ["countries"], message: "Коды стран: +7, +375" });
    } else if (countries.length === 0) {
      ctx.addIssue({ code: "custom", path: ["countries"], message: "Укажите хотя бы одну страну" });
    } else if (countries.length > CODE_LIMITS.countries) {
      ctx.addIssue({
        code: "custom",
        path: ["countries"],
        message: `Не больше ${CODE_LIMITS.countries} стран`,
      });
    }

    const budget = parseRubles(form.budget);
    if (budget === null || budget > CODE_LIMITS.budgetRubles * 100) {
      ctx.addIssue({ code: "custom", path: ["budget"], message: "От 0 до 10 000 000 ₽" });
    }

    if (Number(form.targetPerDay) < Number(form.targetPerHour)) {
      ctx.addIssue({
        code: "custom",
        path: ["targetPerDay"],
        message: "В сутки — не меньше, чем в час",
      });
    }
  });

export type CodeSettingsForm = z.infer<typeof codeSettingsSchema>;

/** «+7, +375» → ["7", "375"]; что-то не код страны — null. Повторы убираем. */
export function parseCountries(value: string): string[] | null {
  const parts = value
    .split(/[\s,;]+/)
    .map((part) => part.replace(/^\+/, ""))
    .filter(Boolean);
  if (parts.some((part) => !/^[1-9]\d{0,3}$/.test(part))) {
    return null;
  }

  return [...new Set(parts)];
}

/** «3 000» или «1 500,50» → копейки; не число — null. */
export function parseRubles(value: string): number | null {
  const normalized = value.replace(/\s/g, "").replace(",", ".");
  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) {
    return null;
  }

  return Math.round(Number(normalized) * 100);
}

const rubles = (kopecks: number) =>
  kopecks % 100 === 0 ? String(kopecks / 100) : (kopecks / 100).toFixed(2).replace(".", ",");

export const toForm = (settings: CodeSettings): CodeSettingsForm => ({
  call: settings.phoneChannels.includes("call"),
  sms: settings.phoneChannels.includes("sms"),
  countries: settings.countries.map((country) => `+${country}`).join(", "),
  budget: rubles(settings.dailyBudget),
  targetPerHour: String(settings.targetPerHour),
  targetPerDay: String(settings.targetPerDay),
  ipPerHour: String(settings.ipPerHour),
  rangePerHour: String(settings.rangePerHour),
  captchaAfter: String(settings.captchaAfter),
});

export const fromForm = (form: CodeSettingsForm): CodeSettingsChanges => ({
  phoneChannels: [...(form.call ? ["call"] : []), ...(form.sms ? ["sms"] : [])] as PhoneChannel[],
  countries: parseCountries(form.countries) ?? [],
  dailyBudget: parseRubles(form.budget) ?? 0,
  targetPerHour: Number(form.targetPerHour),
  targetPerDay: Number(form.targetPerDay),
  ipPerHour: Number(form.ipPerHour),
  rangePerHour: Number(form.rangePerHour),
  captchaAfter: Number(form.captchaAfter),
});
