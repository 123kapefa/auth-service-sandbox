import { z } from "zod";

const HEX = /^#[0-9a-fA-F]{6}$/;

export const isHexColor = (value: string) => HEX.test(value);

/** Поля акцента в формах оформления: свой или нет, светлый и (необязательно) свой для тёмной темы. */
export const accentFields = {
  hasColor: z.boolean(),
  color: z.string(),
  hasColorDark: z.boolean(),
  colorDark: z.string(),
};

export interface AccentForm {
  hasColor: boolean;
  color: string;
  hasColorDark: boolean;
  colorDark: string;
}

/** Заданный цвет — #rrggbb; тёмный проверяется, только если включён вместе с основным. */
export function checkAccent(form: AccentForm, context: z.RefinementCtx): void {
  if (form.hasColor && !isHexColor(form.color)) {
    context.addIssue({ code: "custom", path: ["color"], message: "Цвет в виде #1f6feb" });
  }

  if (form.hasColor && form.hasColorDark && !isHexColor(form.colorDark)) {
    context.addIssue({ code: "custom", path: ["colorDark"], message: "Цвет в виде #1f6feb" });
  }
}

/** Без своего цвета поля всё равно заполнены — чтобы, включив цвет, начать с понятного значения. */
export const toAccentForm = (
  own: { color: string | null; colorDark: string | null },
  start: string | null,
): AccentForm => ({
  hasColor: own.color !== null,
  color: own.color ?? start ?? "#3c3c3c",
  hasColorDark: own.colorDark !== null,
  colorDark: own.colorDark ?? own.color ?? start ?? "#cdcdcd",
});

export const fromAccentForm = (form: AccentForm) => ({
  color: form.hasColor ? form.color.toLowerCase() : null,
  colorDark: form.hasColor && form.hasColorDark ? form.colorDark.toLowerCase() : null,
});

/** Для миниатюры: недописанный цвет — серый, а не «сломанная» страница. */
export const previewAccent = (form: AccentForm): AccentForm => ({
  ...form,
  color: isHexColor(form.color) ? form.color : "#3c3c3c",
  colorDark: isHexColor(form.colorDark) ? form.colorDark : "#cdcdcd",
});
