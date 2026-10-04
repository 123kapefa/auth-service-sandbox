import { useQueryClient, useSuspenseQuery } from "@tanstack/react-query";
import { accountProfileQueries } from "@/entities/account-profile";
import { AccountActionLink, useActionStatus } from "@/features/account-action";
import { formatDateTime } from "@/shared/lib/format";
import { Badge, Card, CardTitle } from "@/shared/ui";

/** Профиль: телефон (логин) и почта. Смена почты — на странице сервера входа; пароль — в «Безопасности». */
export function AccountProfilePage() {
  const queryClient = useQueryClient();
  const { data: profile } = useSuspenseQuery(accountProfileQueries.profile());
  useActionStatus(() => void queryClient.invalidateQueries(accountProfileQueries.profile()));
  // У пользователей из импорта дата создания неизвестна — строку не показываем.
  const createdAt = formatDateTime(profile.createdAt);

  return (
    <div className="space-y-4">
      <h1 className="text-3xl font-bold tracking-tight">Профиль</h1>

      <Card>
        <CardTitle>Телефон</CardTitle>
        <p className="font-medium">{profile.phone ?? "не указан"}</p>
        <p className="mt-1 text-sm text-muted-foreground">
          Это ваш логин. Сменить номер можно через поддержку.
        </p>
      </Card>

      <Card>
        <CardTitle>Почта</CardTitle>
        <div className="flex flex-wrap items-center gap-3">
          {profile.email ? (
            <>
              <span className="font-medium">{profile.email}</span>
              {profile.emailVerified ? (
                <Badge variant="solid">подтверждена</Badge>
              ) : (
                <Badge variant="warning">не подтверждена</Badge>
              )}
            </>
          ) : (
            <span className="text-muted-foreground">не указана</span>
          )}
          <div className="ml-auto">
            <AccountActionLink action="update-email">
              {profile.email ? "Сменить почту" : "Добавить почту"}
            </AccountActionLink>
          </div>
        </div>
        <p className="mt-1 text-sm text-muted-foreground">
          На почту приходят коды входа и ссылка для сброса пароля. Новая почта заменит прежнюю после
          перехода по ссылке из письма.
        </p>
      </Card>

      {createdAt !== "—" && (
        <p className="text-xs text-muted-foreground">Учётная запись создана {createdAt}.</p>
      )}
    </div>
  );
}
