import { z } from "zod";
import { type Branding, BRANDING_NAME_MAX_LENGTH } from "@/entities/branding";
import { accentFields, checkAccent, fromAccentForm, toAccentForm } from "./accent";

/** Как на сервере: название обязательно (до 60), акцент — #rrggbb (тёмный — свой или тот же), тема — одна из трёх. */
export const brandingSchema = z
  .object({
    name: z
      .string()
      .trim()
      .min(1, "Укажите название")
      .max(BRANDING_NAME_MAX_LENGTH, `Не больше ${BRANDING_NAME_MAX_LENGTH} символов`),
    logo: z.string().nullable(),
    ...accentFields,
    theme: z.enum(["system", "light", "dark"]),
  })
  .superRefine(checkAccent);

export type BrandingForm = z.infer<typeof brandingSchema>;

export const toForm = (branding: Branding): BrandingForm => ({
  name: branding.name,
  logo: branding.logo,
  ...toAccentForm(branding, null),
  theme: branding.theme,
});

export const fromForm = (form: BrandingForm): Branding => ({
  name: form.name.trim(),
  logo: form.logo,
  ...fromAccentForm(form),
  theme: form.theme,
});
