import { z } from "zod";
import { type Branding, BRANDING_NAME_MAX_LENGTH } from "@/entities/branding";

/** Как на сервере: название обязательно (до 60), цвет — #rrggbb, тема — одна из трёх. */
export const brandingSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Укажите название")
    .max(BRANDING_NAME_MAX_LENGTH, `Не больше ${BRANDING_NAME_MAX_LENGTH} символов`),
  logo: z.string().nullable(),
  hasColor: z.boolean(),
  color: z.string().regex(/^#[0-9a-fA-F]{6}$/, "Цвет в виде #1f6feb"),
  theme: z.enum(["system", "light", "dark"]),
});

export type BrandingForm = z.infer<typeof brandingSchema>;

/** Без своего цвета поле цвета всё равно заполнено — чтобы, включив цвет, начать с понятного значения. */
const DEFAULT_COLOR = "#3c3c3c";

export const toForm = (branding: Branding): BrandingForm => ({
  name: branding.name,
  logo: branding.logo,
  hasColor: branding.color !== null,
  color: branding.color ?? DEFAULT_COLOR,
  theme: branding.theme,
});

export const fromForm = (form: BrandingForm): Branding => ({
  name: form.name.trim(),
  logo: form.logo,
  color: form.hasColor ? form.color.toLowerCase() : null,
  theme: form.theme,
});
