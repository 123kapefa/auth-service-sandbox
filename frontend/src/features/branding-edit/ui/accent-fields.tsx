import { type UseFormReturn, useWatch } from "react-hook-form";
import { FieldError, Input } from "@/shared/ui";
import { type AccentForm, isHexColor } from "../model/accent";

export const segment =
  "flex h-10 cursor-pointer items-center justify-center border border-primary px-3 text-sm has-checked:bg-primary has-checked:font-semibold has-checked:text-primary-foreground has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-ring";

const legend = "mb-1 font-mono text-[11px] tracking-[0.1em] text-muted-foreground uppercase";

/**
 * Акцент — кнопки, активный пункт меню и фокус (линии, рамки и ошибки остаются своими): серый (или как у проекта) или
 * свой; для тёмной темы — тот же или отдельный, чтобы тёмный цвет не терялся на тёмном фоне.
 */
export function AccentFields({
  form,
  id,
  offLabel,
}: {
  form: UseFormReturn<AccentForm>;
  id: string;
  offLabel: string;
}) {
  const values = useWatch({ control: form.control }) as AccentForm;
  const { errors } = form.formState;
  const set = (field: keyof AccentForm, value: boolean | string) =>
    form.setValue(field, value, { shouldDirty: true, shouldValidate: typeof value === "string" });

  return (
    <fieldset className="grid gap-3">
      <legend className={legend}>Акцент</legend>
      <div className="grid grid-cols-2 gap-2 sm:max-w-sm">
        <label className={segment}>
          <input
            type="radio"
            className="sr-only"
            checked={!values.hasColor}
            onChange={() => set("hasColor", false)}
          />
          {offLabel}
        </label>
        <label className={segment}>
          <input
            type="radio"
            className="sr-only"
            checked={values.hasColor}
            onChange={() => set("hasColor", true)}
          />
          Свой цвет
        </label>
      </div>

      {values.hasColor && (
        <>
          <ColorInput
            form={form}
            field="color"
            id={`${id}-color`}
            label="Светлая тема"
            value={values.color}
            error={errors.color?.message}
          />

          <fieldset className="grid gap-2">
            <legend className={legend}>Тёмная тема</legend>
            <div className="grid grid-cols-2 gap-2 sm:max-w-sm">
              <label className={segment}>
                <input
                  type="radio"
                  className="sr-only"
                  checked={!values.hasColorDark}
                  onChange={() => set("hasColorDark", false)}
                />
                Как в светлой
              </label>
              <label className={segment}>
                <input
                  type="radio"
                  className="sr-only"
                  checked={values.hasColorDark}
                  onChange={() => set("hasColorDark", true)}
                />
                Другой цвет
              </label>
            </div>
            {values.hasColorDark && (
              <ColorInput
                form={form}
                field="colorDark"
                id={`${id}-color-dark`}
                label="Цвет для тёмной темы"
                value={values.colorDark}
                error={errors.colorDark?.message}
              />
            )}
          </fieldset>
        </>
      )}

      <p className="text-xs text-muted-foreground">
        Кнопки, активный пункт меню и фокус. Линии и рамки остаются серыми, ошибки — красными. Текст
        на кнопке — тёмный или светлый, по яркости цвета.
      </p>
    </fieldset>
  );
}

function ColorInput({
  form,
  field,
  id,
  label,
  value,
  error,
}: {
  form: UseFormReturn<AccentForm>;
  field: "color" | "colorDark";
  id: string;
  label: string;
  value: string;
  error?: string;
}) {
  return (
    <div className="grid gap-1">
      <div className="flex items-center gap-2">
        <input
          type="color"
          aria-label={`${label}: выбрать цвет`}
          value={isHexColor(value) ? value.toLowerCase() : "#3c3c3c"}
          onChange={(event) =>
            form.setValue(field, event.target.value, { shouldDirty: true, shouldValidate: true })
          }
          className="h-10 w-12 cursor-pointer border border-border bg-background p-1"
        />
        <Input
          aria-label={`${label}, #rrggbb`}
          aria-invalid={error ? true : undefined}
          aria-describedby={`${id}-error`}
          className="w-32 font-mono"
          {...form.register(field)}
        />
      </div>
      <FieldError id={`${id}-error`} message={error} />
    </div>
  );
}
