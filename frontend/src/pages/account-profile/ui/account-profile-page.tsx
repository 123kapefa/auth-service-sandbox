import { useQuery, useQueryClient, useSuspenseQuery } from "@tanstack/react-query";
import { accountProfileQueries } from "@/entities/account-profile";
import { accountSecurityQueries } from "@/entities/account-security";
import { AccountActionLink, useActionStatus } from "@/features/account-action";
import { AppModesCard } from "@/features/account-mode-switch";
import { formatDate, formatDateTime, formatPhone } from "@/shared/lib/format";
import { Badge, Card, CardTitle } from "@/shared/ui";

/**
 * Профиль (экран 14 этапа 10): телефон (логин), почта и режим в приложениях проекта. Смена номера и почты — на страницах
 * сервера входа (номер — кодом на новый); вход по телефону выключен в проекте — смены номера нет. Пароль — в
 * «Безопасности».
 */
export function AccountProfilePage() {
  const queryClient = useQueryClient();
  const { data: profile } = useSuspenseQuery(accountProfileQueries.profile());
  useActionStatus(() => void queryClient.invalidateQueries(accountProfileQueries.profile()));
  // Не дождались способов проекта — кнопку показываем: сервер входа всё равно проверит.
  const { data: methods } = useQuery(accountSecurityQueries.signInMethods());
  const phoneChangeable = !methods?.projectDisabled.includes("phone");
  const phoneSince = formatDate(profile.phoneChangedAt ?? profile.createdAt);
  // У пользователей из импорта дата создания неизвестна — строку не показываем.
  const createdAt = formatDateTime(profile.createdAt);

  return (
    <div className="space-y-4">
      <h1 className="text-3xl font-bold tracking-tight">Профиль</h1>

      <Card aria-label="Телефон">
        <div className="flex flex-wrap items-start gap-3">
          <div className="min-w-0 flex-[1_1_16rem]">
            <CardTitle className="mb-1">Телефон</CardTitle>
            <p className="text-sm text-muted-foreground">
              Им вы входите и на него приходят коды. Сменили SIM-карту — смените и здесь, иначе
              новый владелец старого номера сможет войти в ваш аккаунт.
            </p>
          </div>
          {phoneChangeable && (
            <div className="ml-auto">
              <AccountActionLink action="update-phone">
                {profile.phone ? "Сменить номер" : "Указать номер"}
              </AccountActionLink>
            </div>
          )}
        </div>
        <p className="mt-3 flex flex-wrap items-baseline gap-3">
          {profile.phone ? (
            <>
              <span className="font-mono font-semibold">{formatPhone(profile.phone)}</span>
              {phoneSince !== "—" && (
                <span className="text-sm text-muted-foreground">с {phoneSince}</span>
              )}
            </>
          ) : (
            <span className="text-muted-foreground">не указан</span>
          )}
        </p>
      </Card>

      <Card aria-label="Почта">
        <div className="flex flex-wrap items-start gap-3">
          <div className="min-w-0 flex-[1_1_16rem]">
            <CardTitle className="mb-1">Почта</CardTitle>
            <p className="text-sm text-muted-foreground">
              Для кодов входа и уведомлений безопасности. Новая почта заменит прежнюю после перехода
              по ссылке из письма.
            </p>
          </div>
          <div className="ml-auto">
            <AccountActionLink action="update-email">
              {profile.email ? "Сменить" : "Добавить почту"}
            </AccountActionLink>
          </div>
        </div>
        <p className="mt-3 flex flex-wrap items-center gap-3">
          {profile.email ? (
            <>
              <span className="font-semibold">{profile.email}</span>
              {profile.emailVerified ? (
                <Badge variant="solid">подтверждена</Badge>
              ) : (
                <Badge variant="warning">не подтверждена</Badge>
              )}
            </>
          ) : (
            <span className="text-muted-foreground">не указана</span>
          )}
        </p>
      </Card>

      <AppModesCard />

      {createdAt !== "—" && (
        <p className="text-xs text-muted-foreground">Учётная запись создана {createdAt}.</p>
      )}
    </div>
  );
}
