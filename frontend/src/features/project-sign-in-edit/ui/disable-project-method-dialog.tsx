import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import {
  type ProjectMethod,
  type ProjectSignIn,
  signInSettingsQueries,
  TRANSITIONAL_METHODS,
} from "@/entities/sign-in-settings";
import { formatCount } from "@/shared/lib/format";
import { Button, Dialog, FieldError, Input } from "@/shared/ui";
import { disableTitle, onlyWay, transitionHint } from "../model/labels";
import {
  daysAhead,
  formatDay,
  keepsRegistration,
  loginFieldAfter,
  withMethod,
} from "../model/rules";
import { useSaveProjectSignIn } from "../model/use-save-project-sign-in";

/**
 * Окно «Выключить …?» (экран 22 этапа 10): сколько людей входят только этим способом (их выключение запрёт), «Сразу» или
 * «С переходным периодом до» (телефон, почта, пароль; по умолчанию — через 30 дней). Считает людей сервис перебором —
 * пока считает, кнопка доступна, число появится.
 */
export function DisableProjectMethodDialog({
  method,
  settings,
  onClose,
}: {
  method: ProjectMethod;
  settings: ProjectSignIn;
  onClose: () => void;
}) {
  const transitional = TRANSITIONAL_METHODS.includes(method);
  const current = settings.methods[method];
  const [mode, setMode] = useState<"now" | "transition">(transitional ? "transition" : "now");
  const [until, setUntil] = useState(
    current.status === "transition" && current.until ? current.until : daysAhead(30),
  );
  const affected = useQuery(signInSettingsQueries.affected(method));
  const mutation = useSaveProjectSignIn(onClose);
  const loginField = loginFieldAfter(settings, method);
  const registration = keepsRegistration(settings, method);

  const min = daysAhead(1);
  const max = daysAhead(365);
  const dateError =
    mode === "transition" && (!until || until < min || until > max)
      ? "Дата — с завтра и не дальше года"
      : undefined;

  const only = affected.data?.only;
  const save = () =>
    mutation.mutate({
      settings: withMethod(
        settings,
        method,
        mode === "transition" ? { status: "transition", until } : { status: "off" },
      ),
      done:
        mode === "transition" ? `Выключится после ${formatDay(until)}` : "Способ входа выключен",
    });

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      eyebrow="Способы входа в проекте"
      title={disableTitle[method]}
    >
      <p className="mb-4 text-sm" aria-live="polite">
        {affected.isPending ? (
          "Считаем, сколько людей входят только так…"
        ) : affected.isError ? (
          "Не удалось посчитать людей — проверьте журнал входов перед выключением."
        ) : only ? (
          <>
            <strong>{formatCount(only)} человек</strong> {onlyWay[method]}. После выключения они не
            смогут войти.
          </>
        ) : (
          <>
            Только этим способом не входит никто
            {affected.data.users > 0 && ` (пользуются — ${formatCount(affected.data.users)})`}: все
            смогут войти иначе.
          </>
        )}
      </p>

      {transitional && (
        <fieldset className="mb-4 grid gap-3 text-sm">
          <legend className="sr-only">Когда выключить</legend>
          <label className="flex items-start gap-2.5">
            <input
              type="radio"
              name="disable-mode"
              className="mt-0.5"
              checked={mode === "now"}
              onChange={() => setMode("now")}
            />
            <span>
              <strong>Сразу.</strong>{" "}
              {only ? "Эти люди смогут войти только через поддержку." : "Способ пропадёт сейчас."}
            </span>
          </label>
          <div className="flex items-start gap-2.5">
            <input
              id="disable-mode-transition"
              type="radio"
              name="disable-mode"
              className="mt-0.5"
              checked={mode === "transition"}
              onChange={() => setMode("transition")}
            />
            <div className="grid gap-1">
              <div className="flex flex-wrap items-center gap-2">
                <label htmlFor="disable-mode-transition" className="font-semibold">
                  С переходным периодом до
                </label>
                <Input
                  type="date"
                  aria-label="Конец переходного периода"
                  className="h-8 w-40 px-2 font-mono"
                  min={min}
                  max={max}
                  value={until}
                  aria-invalid={dateError ? true : undefined}
                  aria-describedby="disable-until-error"
                  onChange={(event) => {
                    setUntil(event.target.value);
                    setMode("transition");
                  }}
                />
              </div>
              <span className="text-muted-foreground">{transitionHint[method]}</span>
              <FieldError id="disable-until-error" message={dateError} />
            </div>
          </div>
        </fieldset>
      )}

      {!registration && (
        <p className="mb-4 border border-dashed border-primary bg-hatch p-3 text-sm">
          Это последний способ регистрации: сначала включите телефон, почту, Яндекс ID или
          Госуслуги.
        </p>
      )}

      <p className="mb-5 text-sm text-muted-foreground">
        Изменение попадёт в журнал.{loginField && ` Поле входа станет «${loginField}».`}
      </p>
      <div className="flex flex-wrap gap-2">
        <Button disabled={mutation.isPending || !registration || !!dateError} onClick={save}>
          {mode === "transition" ? "Выключить с переходом" : "Выключить"}
        </Button>
        <Button variant="outline" onClick={onClose}>
          Отмена
        </Button>
      </div>
    </Dialog>
  );
}
