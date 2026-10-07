import { zodResolver } from "@hookform/resolvers/zod";
import { Fragment, type ReactNode } from "react";
import { useForm, useWatch } from "react-hook-form";
import { type CodeSettings, type ProjectSignIn } from "@/entities/sign-in-settings";
import { cn } from "@/shared/lib/cn";
import { Badge, Button, Card, CardTitle, FieldError, Input } from "@/shared/ui";
import {
  type CodeSettingsForm as Form,
  codeSettingsSchema,
  fromForm,
  toForm,
} from "../model/schemas";
import { useSaveCodeSettings } from "../model/use-save-code-settings";

const row = "grid items-center gap-x-6 gap-y-1 sm:grid-cols-[17rem_minmax(0,1fr)]";
const small = "h-9 w-28 font-mono";

/**
 * Каналы и защита от накрутки (экран 15 этапа 10): звонки и SMS, страны, дневной бюджет, лимиты отправки кодов и капча.
 * Одна форма на две карточки, «Сохранить» — в первой; сохраняется всё целиком. methods — способы входа проекта: по ним
 * в порядке каналов видно, что выключено.
 */
export function CodeSettingsEditor({
  settings,
  methods,
}: {
  settings: CodeSettings;
  methods?: ProjectSignIn;
}) {
  const mutation = useSaveCodeSettings();
  const form = useForm<Form>({
    resolver: zodResolver(codeSettingsSchema),
    defaultValues: toForm(settings),
  });
  const values = useWatch({ control: form.control }) as Form;
  const { errors, isDirty } = form.formState;

  const submit = form.handleSubmit((values) =>
    mutation.mutate(fromForm(values), { onSuccess: (saved) => form.reset(toForm(saved)) }),
  );

  const off = (method: "passkey" | "email" | "phone") => methods?.methods[method].status === "off";
  const order = [
    { label: "Ключ доступа", on: !off("passkey") },
    { label: "Почта", on: !off("email") },
    { label: "Звонок", on: values.call && !off("phone") },
    { label: "SMS", on: values.sms && !off("phone") },
  ];

  return (
    <form onSubmit={submit} className="grid gap-4" noValidate aria-label="Каналы и защита">
      <Card aria-label="Каналы">
        <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle className="mb-1">Каналы</CardTitle>
            <p className="text-sm text-muted-foreground">
              Порядок — от дешёвого к дорогому. SMS отправляется, только если звонок не дошёл.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            {isDirty && (
              <Button type="button" variant="outline" onClick={() => form.reset(toForm(settings))}>
                Отменить изменения
              </Button>
            )}
            <Button type="submit" disabled={mutation.isPending || !isDirty}>
              Сохранить
            </Button>
          </div>
        </div>

        <div className="grid gap-3 text-sm">
          <div className={row}>
            <span className="text-muted-foreground">Порядок</span>
            <ol className="flex flex-wrap items-center gap-2" aria-label="Порядок каналов">
              {order.map((step, index) => (
                <Fragment key={step.label}>
                  {index > 0 && (
                    <span aria-hidden className="text-xs text-muted-foreground">
                      →
                    </span>
                  )}
                  <li
                    className={cn(
                      "inline-flex h-8 items-center border border-primary px-3",
                      !step.on && "border-dashed bg-hatch text-muted-foreground line-through",
                    )}
                  >
                    {step.label}
                    {!step.on && <span className="sr-only"> (выключено)</span>}
                  </li>
                </Fragment>
              ))}
            </ol>
          </div>

          <div className={row}>
            <span className="text-muted-foreground">Звонки</span>
            <Toggle on={values.call}>
              <input type="checkbox" aria-label="Звонки" {...form.register("call")} />
            </Toggle>
          </div>
          <div className={row}>
            <span className="text-muted-foreground">SMS</span>
            <div className="grid gap-1">
              <Toggle on={values.sms} note="запасной канал">
                <input
                  type="checkbox"
                  aria-label="SMS"
                  aria-describedby="codes-sms-error"
                  {...form.register("sms")}
                />
              </Toggle>
              <FieldError id="codes-sms-error" message={errors.sms?.message} />
            </div>
          </div>

          <Field id="codes-countries" label="Страны" error={errors.countries?.message}>
            <Input
              id="codes-countries"
              className="h-9 w-48 font-mono"
              placeholder="+7"
              aria-invalid={errors.countries ? true : undefined}
              aria-describedby="codes-countries-error"
              {...form.register("countries")}
            />
          </Field>
          <Field
            id="codes-budget"
            label="Дневной бюджет, ₽"
            error={errors.budget?.message}
            hint="0 — без бюджета"
          >
            <Input
              id="codes-budget"
              inputMode="decimal"
              className="h-9 w-36 font-mono"
              aria-invalid={errors.budget ? true : undefined}
              aria-describedby="codes-budget-error"
              {...form.register("budget")}
            />
          </Field>
        </div>
      </Card>

      <Card aria-label="Защита от накрутки">
        <CardTitle className="mb-4">Защита от накрутки</CardTitle>
        <div className="grid gap-3 text-sm">
          <Field
            id="codes-target-hour"
            label="На номер"
            error={errors.targetPerHour?.message ?? errors.targetPerDay?.message}
          >
            <Input
              id="codes-target-hour"
              inputMode="numeric"
              aria-label="На номер в час"
              className={small}
              aria-invalid={errors.targetPerHour ? true : undefined}
              {...form.register("targetPerHour")}
            />
            <span>в час,</span>
            <Input
              inputMode="numeric"
              aria-label="На номер в сутки"
              className={small}
              aria-invalid={errors.targetPerDay ? true : undefined}
              {...form.register("targetPerDay")}
            />
            <span>в сутки</span>
          </Field>
          <Field id="codes-ip" label="С одного IP" error={errors.ipPerHour?.message}>
            <Input
              id="codes-ip"
              inputMode="numeric"
              className={small}
              aria-invalid={errors.ipPerHour ? true : undefined}
              {...form.register("ipPerHour")}
            />
            <span>в час</span>
          </Field>
          <Field
            id="codes-range"
            label={`На диапазон номеров (${settings.rangeDigits} цифр)`}
            error={errors.rangePerHour?.message}
          >
            <Input
              id="codes-range"
              inputMode="numeric"
              className={small}
              aria-invalid={errors.rangePerHour ? true : undefined}
              {...form.register("rangePerHour")}
            />
            <span>в час</span>
          </Field>
          <Field id="codes-captcha" label="Капча" error={errors.captchaAfter?.message}>
            <span>после</span>
            <Input
              id="codes-captcha"
              inputMode="numeric"
              className={small}
              aria-invalid={errors.captchaAfter ? true : undefined}
              {...form.register("captchaAfter")}
            />
            <span>отправок за час и при любом сработавшем лимите</span>
          </Field>
        </div>
      </Card>
    </form>
  );
}

function Toggle({ on, note, children }: { on: boolean; note?: string; children: ReactNode }) {
  return (
    <label className="flex flex-wrap items-center gap-2.5">
      {children}
      <Badge variant={on ? "solid" : "warning"}>{on ? "включены" : "выключены"}</Badge>
      {note && <span>{note}</span>}
    </label>
  );
}

function Field({
  id,
  label,
  error,
  hint,
  children,
}: {
  id: string;
  label: string;
  error?: string;
  hint?: string;
  children: ReactNode;
}) {
  return (
    <div className={row}>
      <label htmlFor={id} className="text-muted-foreground">
        {label}
      </label>
      <div className="grid gap-1">
        <div className="flex flex-wrap items-center gap-2">
          {children}
          {hint && <span className="text-xs text-muted-foreground">{hint}</span>}
        </div>
        <FieldError id={`${id}-error`} message={error} />
      </div>
    </div>
  );
}
