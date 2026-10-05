import { useQuery } from "@tanstack/react-query";
import { appModeQueries } from "@/entities/app-mode";
import { Button, Card, CardTitle } from "@/shared/ui";
import { useSwitchMode } from "../model/use-switch-mode";

/**
 * Режим в приложениях проекта — где у пользователя больше одной роли (сотрудник-покупатель). Страница приложения ходит
 * со своим токеном не в сервис входа, поэтому режим переключают здесь; приложение получит права нового режима, обновив
 * токен (без повторного входа). Ролей по одной — карточки нет.
 */
export function AppModesCard() {
  const { data } = useQuery(appModeQueries.list());
  const switchMode = useSwitchMode();
  const apps = (data ?? []).filter((app) => app.modes.length > 1);
  if (apps.length === 0) {
    return null;
  }

  return (
    <Card>
      <CardTitle>Режим в приложениях</CardTitle>
      <ul className="grid gap-3">
        {apps.map((app) => (
          <li key={app.app} className="flex flex-wrap items-center gap-2">
            <span className="mr-auto font-medium">{app.name ?? app.app}</span>
            {app.modes.map((mode) => (
              <Button
                key={mode}
                size="sm"
                variant={mode === app.activeMode ? "default" : "outline"}
                aria-pressed={mode === app.activeMode}
                disabled={switchMode.isPending}
                onClick={() =>
                  mode !== app.activeMode && switchMode.mutate({ app: app.app, activeMode: mode })
                }
              >
                {mode}
              </Button>
            ))}
          </li>
        ))}
      </ul>
      <p className="mt-2 text-sm text-muted-foreground">
        Режим — какие права у вас в приложении сейчас. Открытое приложение подхватит новый режим
        само.
      </p>
    </Card>
  );
}
