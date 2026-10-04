import { z } from "zod";
import { type AppBrandingOwn, BRANDING_NAME_MAX_LENGTH } from "@/entities/branding";

/**
 * Оформление приложения, как на сервере: пустое название — как у проекта (иначе до 60), логотип — свой или как у
 * проекта (null), цвет — #rrggbb, если свой.
 */
export const appBrandingSchema = z
  .object({
    name: z
      .string()
      .trim()
      .max(BRANDING_NAME_MAX_LENGTH, `Не больше ${BRANDING_NAME_MAX_LENGTH} символов`),
    logo: z.string().nullable(),
    hasColor: z.boolean(),
    color: z.string(),
  })
  .superRefine((form, context) => {
    if (form.hasColor && !/^#[0-9a-fA-F]{6}$/.test(form.color)) {
      context.addIssue({ code: "custom", path: ["color"], message: "Цвет в виде #1f6feb" });
    }
  });

export type AppBrandingForm = z.infer<typeof appBrandingSchema>;

export const toAppForm = (own: AppBrandingOwn, projectColor: string | null): AppBrandingForm => ({
  name: own.name ?? "",
  logo: own.logo,
  hasColor: own.color !== null,
  // Включили свой цвет — начать с цвета проекта (или серого).
  color: own.color ?? projectColor ?? "#3c3c3c",
});

export const fromAppForm = (form: AppBrandingForm): AppBrandingOwn => ({
  name: form.name.trim() === "" ? null : form.name.trim(),
  logo: form.logo,
  color: form.hasColor ? form.color.toLowerCase() : null,
});
