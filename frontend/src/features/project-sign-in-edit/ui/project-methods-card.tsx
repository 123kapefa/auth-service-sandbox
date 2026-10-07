import { type ReactNode, useState } from "react";
import {
  PROJECT_METHODS,
  type ProjectMethod,
  type ProjectMethodState,
  type ProjectSignIn,
} from "@/entities/sign-in-settings";
import { Badge, Button, Card, CardTitle, ConfirmDialog } from "@/shared/ui";
import { methodHint, methodLabel } from "../model/labels";
import { formatDay, withMethod } from "../model/rules";
import { useSaveProjectSignIn } from "../model/use-save-project-sign-in";
import { DisableProjectMethodDialog } from "./disable-project-method-dialog";

/**
 * «Способы входа в проекте» (экран 22 этапа 10): что вообще доступно людям проекта. Выключение — с окном (сколько людей
 * входят только так, сразу или с переходным периодом), включение — без окна. Внешний вход без ключей не включить.
 * 2FA — обязательна или по желанию.
 */
export function ProjectMethodsCard({ settings }: { settings: ProjectSignIn }) {
  const [disabling, setDisabling] = useState<ProjectMethod | null>(null);
  const [twoFactor, setTwoFactor] = useState(false);
  const save = useSaveProjectSignIn();
  const saveTwoFactor = useSaveProjectSignIn(() => setTwoFactor(false));
  const required = settings.twoFactor === "required";

  const enable = (method: ProjectMethod) =>
    save.mutate({
      settings: withMethod(settings, method, { status: "on" }),
      done: `${methodLabel[method]}: включено`,
    });

  return (
    <Card aria-label="Способы входа в проекте">
      <CardTitle className="mb-1">Способы входа в проекте</CardTitle>
      <p className="text-sm text-muted-foreground">
        Что вообще доступно людям этого проекта. Выключенное пропадает со страниц входа, из кабинета
        и регистрации.
      </p>
      <ul className="mt-4 divide-y divide-border border-t border-border">
        {PROJECT_METHODS.map((method) => {
          const state = settings.methods[method];
          const noKeys = state.configured === false;
          return (
            <li key={method}>
              <Row
                title={methodLabel[method]}
                badge={<StateBadge state={state} />}
                hint={noKeys ? "нужны ключи проекта — без них не включить" : hint(method, state)}
                action={
                  state.status === "on" ? (
                    <Button
                      size="sm"
                      variant="outline"
                      aria-label={`Выключить: ${methodLabel[method]}`}
                      onClick={() => setDisabling(method)}
                    >
                      Выключить
                    </Button>
                  ) : (
                    <>
                      {state.status === "transition" && (
                        <Button
                          size="sm"
                          variant="outline"
                          aria-label={`Изменить срок: ${methodLabel[method]}`}
                          onClick={() => setDisabling(method)}
                        >
                          Изменить срок
                        </Button>
                      )}
                      <Button
                        size="sm"
                        variant="outline"
                        disabled={noKeys || save.isPending}
                        aria-label={`Включить: ${methodLabel[method]}`}
                        onClick={() => enable(method)}
                      >
                        Включить
                      </Button>
                    </>
                  )
                }
              />
            </li>
          );
        })}
        <li>
          <Row
            title="2FA"
            badge={
              required ? <Badge variant="solid">обязательна</Badge> : <Badge>по желанию</Badge>
            }
            hint={
              required
                ? "без приложения-аутентификатора при входе сразу просят его подключить"
                : "можно подключить приложение-аутентификатор в кабинете"
            }
            action={
              <Button size="sm" variant="outline" onClick={() => setTwoFactor(true)}>
                {required ? "Сделать по желанию" : "Сделать обязательной"}
              </Button>
            }
          />
        </li>
      </ul>
      {disabling && (
        <DisableProjectMethodDialog
          method={disabling}
          settings={settings}
          onClose={() => setDisabling(null)}
        />
      )}
      <ConfirmDialog
        open={twoFactor}
        onOpenChange={setTwoFactor}
        title={required ? "Сделать 2FA по желанию?" : "Сделать 2FA обязательной?"}
        description={
          required
            ? "Подключать приложение-аутентификатор больше не будут просить. Кто уже подключил — продолжит вводить код. Изменение попадёт в журнал."
            : "Каждого, у кого нет приложения-аутентификатора, при следующем входе попросят его подключить — без этого не войти. Изменение попадёт в журнал."
        }
        confirmLabel={required ? "Сделать по желанию" : "Сделать обязательной"}
        pending={saveTwoFactor.isPending}
        onConfirm={() =>
          saveTwoFactor.mutate({
            settings: { ...settings, twoFactor: required ? "optional" : "required" },
            done: required ? "2FA — по желанию" : "2FA обязательна",
          })
        }
      />
    </Card>
  );
}

function hint(method: ProjectMethod, state: ProjectMethodState): string {
  if (state.status === "transition" && state.until) {
    return `переходный период: до ${formatDay(state.until)} — только у тех, кто входит лишь так; новых не регистрируем`;
  }

  return methodHint[method];
}

function StateBadge({ state }: { state: ProjectMethodState }) {
  if (state.status === "on") {
    return <Badge variant="solid">включён</Badge>;
  }

  return state.status === "transition" && state.until ? (
    <Badge variant="warning">до {formatDay(state.until)}</Badge>
  ) : (
    <Badge variant="warning">выключен</Badge>
  );
}

function Row({
  title,
  badge,
  hint,
  action,
}: {
  title: string;
  badge: ReactNode;
  hint: string;
  action: ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-center gap-3 py-3.5">
      <div className="min-w-0 flex-[1_1_16rem]">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-semibold">{title}</span>
          {badge}
        </div>
        <p className="mt-0.5 text-sm text-muted-foreground">{hint}</p>
      </div>
      <div className="ml-auto flex flex-wrap gap-2">{action}</div>
    </div>
  );
}
