import { useMutation } from "@tanstack/react-query";
import { adminSessionApi } from "@/entities/admin-session";
import { ACCOUNT_BASE, ADMIN_BASE, loginUrl } from "@/shared/config/routes";
import { Button, Card } from "@/shared/ui";

/**
 * Вошёл, но управлять нечем: покупатель или сотрудник не в том режиме. «Войти другим пользователем» — выход (сессия
 * Keycloak тоже завершается) и сразу вход с возвратом в админку.
 */
export function NoAccessPage({ activeMode }: { activeMode: string | null }) {
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
          Если вы сотрудник — войдите своей рабочей учётной записью.
        </p>
        <div className="flex flex-wrap gap-2">
          <Button onClick={() => switchUser.mutate()} disabled={switchUser.isPending}>
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
