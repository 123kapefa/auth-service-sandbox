import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import {
  type IssuedServiceKey,
  SecretDialog,
  SERVICE_PERMISSIONS,
  type ServiceKey,
  serviceKeyQueries,
} from "@/entities/service-key";
import { Button, ConfirmDialog, Dialog } from "@/shared/ui";
import {
  useRevokeServiceKey,
  useRotateServiceKey,
  useSetServicePermissions,
} from "../model/use-service-key-actions";

/**
 * Действия с ключом сервиса: права (в сервисе входа и в приложениях проекта), перевыпуск, отзыв. Перевыпуск: старый
 * секрет сразу недействителен; выданные по нему токены — тоже сразу (ключ мог утечь) или доживают свой срок
 * (плановая смена: сервис с секретом в файле подхватит новый без остановки).
 */
export function ServiceKeyActions({ service }: { service: ServiceKey }) {
  const [dialog, setDialog] = useState<"permissions" | "rotate" | "revoke" | null>(null);
  const [issued, setIssued] = useState<IssuedServiceKey | null>(null);
  const [permissions, setPermissions] = useState<string[]>(service.permissions ?? []);
  const [keepTokens, setKeepTokens] = useState(false);
  const grantable = useQuery({
    ...serviceKeyQueries.grantable(),
    enabled: dialog === "permissions",
  });
  const rotate = useRotateServiceKey();
  const revoke = useRevokeServiceKey();
  const setServicePermissions = useSetServicePermissions();

  const toggle = (name: string) =>
    setPermissions((current) =>
      current.includes(name) ? current.filter((item) => item !== name) : [...current, name],
    );

  return (
    <span className="flex flex-wrap gap-1">
      <Button
        size="sm"
        variant="ghost"
        disabled={service.permissions === null}
        onClick={() => {
          setPermissions(service.permissions ?? []);
          setDialog("permissions");
        }}
      >
        Права
      </Button>
      <Button size="sm" variant="ghost" onClick={() => setDialog("rotate")}>
        Перевыпустить
      </Button>
      <Button size="sm" variant="ghost" onClick={() => setDialog("revoke")}>
        Отозвать
      </Button>

      <Dialog
        open={dialog === "permissions"}
        onOpenChange={(open) => !open && setDialog(null)}
        title={`Права сервиса «${service.name}»`}
        description="Права сузили — выданные сервису токены сразу перестанут действовать, он возьмёт новый."
      >
        <fieldset className="grid gap-2">
          <legend className="sr-only">Что можно в сервисе входа</legend>
          {SERVICE_PERMISSIONS.map((permission) => (
            <label key={permission.name} className="flex items-start gap-2 text-sm">
              <input
                type="checkbox"
                className="mt-1"
                checked={permissions.includes(permission.name)}
                onChange={() => toggle(permission.name)}
              />
              <span>
                {permission.label}
                <span className="block text-xs text-muted-foreground">{permission.hint}</span>
              </span>
            </label>
          ))}
        </fieldset>
        {(grantable.data ?? []).map((app) => (
          <fieldset key={app.app} className="mt-4 grid gap-2">
            <legend className="mb-1 text-sm font-medium">
              В приложении «{app.name ?? app.app}»
            </legend>
            {app.permissions.map((permission) => (
              <label key={permission.key} className="flex items-start gap-2 text-sm">
                <input
                  type="checkbox"
                  className="mt-1"
                  checked={permissions.includes(permission.key)}
                  onChange={() => toggle(permission.key)}
                />
                <span>
                  {permission.description ?? permission.name}
                  <span className="block font-mono text-xs text-muted-foreground">
                    {permission.name}
                  </span>
                </span>
              </label>
            ))}
          </fieldset>
        ))}
        <div className="mt-4 flex justify-end gap-2">
          <Button variant="outline" onClick={() => setDialog(null)}>
            Отмена
          </Button>
          <Button
            disabled={setServicePermissions.isPending}
            onClick={() =>
              setServicePermissions.mutate(
                { code: service.code, permissions },
                { onSuccess: () => setDialog(null) },
              )
            }
          >
            Сохранить
          </Button>
        </div>
      </Dialog>

      <Dialog
        open={dialog === "rotate"}
        onOpenChange={(open) => !open && setDialog(null)}
        title={`Перевыпустить ключ «${service.name}»?`}
        description="Старый секрет сразу перестанет работать — новый нужно сразу положить в настройки сервиса."
      >
        <label className="flex items-start gap-2 text-sm">
          <input
            type="checkbox"
            className="mt-1"
            checked={keepTokens}
            onChange={() => setKeepTokens((current) => !current)}
          />
          <span>
            Плановая смена — без остановки
            <span className="block text-xs text-muted-foreground">
              Выданные токены доживут свой срок (минуты): сервис, читающий секрет из файла, успеет
              взять новый. Если ключ мог утечь — не отмечайте: токены перестанут работать сразу.
            </span>
          </span>
        </label>
        <div className="mt-4 flex justify-end gap-2">
          <Button variant="outline" onClick={() => setDialog(null)}>
            Отмена
          </Button>
          <Button
            variant="destructive"
            disabled={rotate.isPending}
            onClick={() =>
              rotate.mutate(
                { code: service.code, keepTokens },
                {
                  onSuccess: (result) => {
                    setDialog(null);
                    setKeepTokens(false);
                    setIssued(result);
                    rotate.reset(); // секрет — только в окне, не в состоянии мутации
                  },
                },
              )
            }
          >
            Перевыпустить
          </Button>
        </div>
      </Dialog>
      <ConfirmDialog
        open={dialog === "revoke"}
        onOpenChange={(open) => !open && setDialog(null)}
        title={`Отозвать ключ «${service.name}»?`}
        description="Сервис сразу потеряет доступ: новых токенов не получит, выданные перестанут действовать."
        confirmLabel="Отозвать"
        destructive
        pending={revoke.isPending}
        onConfirm={() => revoke.mutate(service.code, { onSuccess: () => setDialog(null) })}
      />
      <SecretDialog issued={issued} onClose={() => setIssued(null)} />
    </span>
  );
}
