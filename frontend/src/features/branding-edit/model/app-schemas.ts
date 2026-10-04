import { z } from "zod";
import { type AppBrandingOwn, BRANDING_NAME_MAX_LENGTH } from "@/entities/branding";
import { accentFields, checkAccent, fromAccentForm, toAccentForm } from "./accent";

/**
 * Оформление приложения, как на сервере: пустое название — как у проекта (иначе до 60), логотип — свой или как у
 * проекта (null), акцент — #rrggbb, если свой (тёмный — свой или тот же).
 */
export const appBrandingSchema = z
  .object({
    name: z
      .string()
      .trim()
      .max(BRANDING_NAME_MAX_LENGTH, `Не больше ${BRANDING_NAME_MAX_LENGTH} символов`),
    logo: z.string().nullable(),
    ...accentFields,
  })
  .superRefine(checkAccent);

export type AppBrandingForm = z.infer<typeof appBrandingSchema>;

// Включили свой цвет — начать с цвета проекта (или серого).
export const toAppForm = (own: AppBrandingOwn, projectColor: string | null): AppBrandingForm => ({
  name: own.name ?? "",
  logo: own.logo,
  ...toAccentForm(own, projectColor),
});

export const fromAppForm = (form: AppBrandingForm): AppBrandingOwn => ({
  name: form.name.trim() === "" ? null : form.name.trim(),
  logo: form.logo,
  ...fromAccentForm(form),
});
