import { useMutation, useQuery } from "@tanstack/react-query";
import { adminSessionApi, adminSessionQueries } from "@/entities/admin-session";
import { getErrorMessage } from "@/shared/api";
import { ACCOUNT_BASE, ADMIN_BASE, loginUrl } from "@/shared/config/routes";
import { Button, Card, FieldError } from "@/shared/ui";

/**
 * Вошёл, но управлять нечем: покупатель или сотрудник не в том режиме. Сотрудник переключает режим прямо здесь (права
 * в новом режиме проверит сервер); «Войти другим пользователем» — выход (сессия Keycloak тоже завершается) и сразу вход
 * с возвратом в админку.
 */
export function NoAccessPage({ activeMode }: { activeMode: string | null }) {
  const modes = useQuery(adminSessionQueries.modes());
  const otherModes = (modes.data ?? []).filter((mode) => mode !== activeMode);
  const switchMode = useMutation({
    mutationFn: adminSessionApi.switchMode,
    onSuccess: () => window.location.reload(),
  });
  const switchUser = useMutation({
    mutationFn: adminSessionApi.logout,
    onSettled: () => window.location.assign(loginUrl(`${ADMIN_BASE}/`)),
  });

  return (
    <div className="mx-auto max-w-lg px-4 py-16">
      <Card>
        <h1 className="mb-3 text-2xl font-bold tracking-tight">Нет доступа к админке</h1>
        <p className="mb-4 text-sm text-muted-foreground">
          В режиме «{activeMode ?? "—"}» прав на управление пользователями, ролями или журналом нет.
          {otherModes.length > 0
            ? " Переключитесь в рабочий режим или войдите другим пользователем."
            : " Если вы сотрудник — войдите своей рабочей учётной записью."}
        </p>
        {otherModes.length > 0 && (
          <div className="mb-3 flex flex-wrap gap-2">
            {otherModes.map((mode) => (
              <Button
                key={mode}
                onClick={() => switchMode.mutate(mode)}
                disabled={switchMode.isPending}
              >
                Режим «{mode}»
              </Button>
            ))}
          </div>
        )}
        {switchMode.isError && (
          <div className="mb-3">
            <FieldError
              id="switch-mode-error"
              message={getErrorMessage(switchMode.error, "Не удалось сменить режим")}
            />
          </div>
        )}
        <div className="flex flex-wrap gap-2">
          <Button
            variant={otherModes.length > 0 ? "outline" : "default"}
            onClick={() => switchUser.mutate()}
            disabled={switchUser.isPending}
          >
            Войти другим пользователем
          </Button>
          <Button asChild variant="outline">
            <a href={`${ACCOUNT_BASE}/`}>В личный кабинет</a>
          </Button>
        </div>
      </Card>
    </div>
  );
}
