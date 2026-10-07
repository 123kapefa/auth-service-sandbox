import { useState } from "react";

/**
 * Поле кода ячейками по цифре. Ячейки — только вид; поле одно и прозрачное поверх них: автоподстановка кода из SMS,
 * вставка и ввод работают как у обычного поля. Заполненная ячейка — с жёсткой тенью, текущая — с рамкой primary.
 * Короткий код (4 цифры звонка) — квадратами слева, длинный — на всю ширину.
 */
export function CodeCells({
  id,
  name,
  length,
  value,
  onChange,
  invalid,
}: {
  id: string;
  name: string;
  length: number;
  value: string;
  onChange: (digits: string) => void;
  invalid?: boolean;
}) {
  const [focused, setFocused] = useState(false);
  const short = length <= 4;
  return (
    <div className={`relative ${short ? "w-fit" : ""}`}>
      <div
        aria-hidden
        className="grid gap-2.5"
        style={{ gridTemplateColumns: `repeat(${length}, ${short ? "3.25rem" : "1fr"})` }}
      >
        {Array.from({ length }, (_, i) => {
          const current = focused && i === Math.min(value.length, length - 1);
          return (
            <div
              key={i}
              className={`flex h-14 items-center justify-center border bg-background font-mono text-2xl transition-colors ${
                invalid
                  ? "border-dashed border-primary bg-hatch"
                  : current || value[i]
                    ? "border-primary"
                    : "border-primary/70"
              } ${value[i] ? "shadow-hard" : ""}`}
            >
              {value[i] ??
                (focused && i === value.length ? (
                  <span className="h-7 w-px animate-pulse bg-foreground" />
                ) : null)}
            </div>
          );
        })}
      </div>
      <input
        id={id}
        name={name}
        value={value}
        onChange={(event) => onChange(event.target.value.replace(/\D/g, "").slice(0, length))}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        className="absolute inset-0 h-full w-full cursor-text bg-transparent text-transparent caret-transparent outline-none selection:bg-transparent"
        autoComplete="one-time-code"
        inputMode="numeric"
        aria-invalid={invalid || undefined}
        autoFocus
      />
    </div>
  );
}
