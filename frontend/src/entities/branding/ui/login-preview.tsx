import { type CSSProperties } from "react";
import { accentFor, PALETTES, textOn } from "../model";
import { type Branding } from "../types";

/**
 * Страница входа в миниатюре — как её увидит посетитель с этим оформлением: название и логотип над карточкой, кнопка и
 * квадрат у названия — акцентом проекта (в тёмной теме — его тёмным цветом), линии и уголок карточки — серые.
 * Палитра — своя (светлая или тёмная), не тема админки.
 */
export function LoginPreview({
  branding,
  scheme,
}: {
  branding: Branding;
  scheme: "light" | "dark";
}) {
  const palette = PALETTES[scheme];
  const accent = accentFor(branding, scheme);
  const style = {
    "--p-bg": palette.background,
    "--p-card": palette.card,
    "--p-fg": palette.foreground,
    "--p-muted": palette.muted,
    "--p-border": palette.border,
    "--p-primary": palette.primary,
    "--p-accent": accent ?? palette.primary,
    "--p-accent-fg": accent ? textOn(accent) : palette.background,
    "--p-shadow": palette.shadow,
  } as CSSProperties;

  return (
    <figure
      aria-label={`Страница входа: ${scheme === "dark" ? "тёмная" : "светлая"} тема`}
      style={style}
      className="grid min-w-0 gap-3 border border-border bg-[var(--p-bg)] px-4 py-6 text-[var(--p-fg)]"
    >
      <div className="flex min-w-0 items-center justify-center gap-2">
        {branding.logo ? (
          <img src={branding.logo} alt="Логотип" className="h-7 max-w-24 object-contain" />
        ) : (
          <span aria-hidden className="size-2.5 shrink-0 bg-[var(--p-accent)]" />
        )}
        <span className="truncate font-mono text-[10px] font-medium tracking-[0.16em] text-[var(--p-muted)] uppercase">
          {branding.name.trim() || "Название проекта"}
        </span>
      </div>
      <div className="relative grid gap-2 border border-[var(--p-border)] bg-[var(--p-card)] p-4">
        <span aria-hidden className="absolute -top-px -left-px size-2 bg-[var(--p-primary)]" />
        <span className="text-sm font-bold">Вход</span>
        <span className="font-mono text-[10px] tracking-[0.1em] text-[var(--p-muted)] uppercase">
          Телефон или почта
        </span>
        <span className="h-7 border border-[var(--p-border)] bg-[var(--p-bg)]" />
        <span className="mt-1 grid h-7 place-items-center bg-[var(--p-accent)] text-xs font-semibold text-[var(--p-accent-fg)] shadow-[3px_3px_0_var(--p-shadow)]">
          Войти
        </span>
      </div>
      <figcaption className="text-center font-mono text-[10px] tracking-[0.1em] text-[var(--p-muted)] uppercase">
        {scheme === "dark" ? "Тёмная" : "Светлая"}
      </figcaption>
    </figure>
  );
}
