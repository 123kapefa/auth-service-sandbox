import { useQueryClient, useSuspenseQuery } from "@tanstack/react-query";
import { type ReactNode } from "react";
import { type AccountCredential, accountSecurityQueries } from "@/entities/account-security";
import { AccountActionLink, useActionStatus } from "@/features/account-action";
import { formatDateTime } from "@/shared/lib/format";
import { Card, CardTitle } from "@/shared/ui";

/**
 * Безопасность: приложение-аутентификатор (2FA), коды восстановления, ключи доступа. Подключить и удалить — на
 * страницах сервера входа: там же он спросит пароль, если вход был давно, и подтверждение удаления.
 */
export function AccountSecurityPage() {
  const queryClient = useQueryClient();
  const { data: security } = useSuspenseQuery(accountSecurityQueries.security());
  useActionStatus(() => void queryClient.invalidateQueries(accountSecurityQueries.security()));
  const codes = security.recoveryCodes;
  const hasApp = security.authenticators.length > 0;

  return (
    <div className="space-y-4">
      <h1 className="text-lg font-semibold">Безопасность</h1>

      <Section
        title="Пароль"
        hint={
          security.hasPassword
            ? "Задан. Новый вводится на странице входа — сервис его не видит."
            : "Не задан: входите по коду из SMS или письма либо ключом доступа."
        }
        action={
          <AccountActionLink action="update-password">
            {security.hasPassword ? "Сменить" : "Задать"}
          </AccountActionLink>
        }
      />

      <Section
        title="Вход с кодом из приложения (2FA)"
        hint="После пароля спросим одноразовый код из приложения-аутентификатора на телефоне (Яндекс Ключ, Google Authenticator и другие)."
        action={
          <AccountActionLink action="configure-totp">
            {hasApp ? "Подключить ещё" : "Подключить"}
          </AccountActionLink>
        }
      >
        <CredentialList
          items={security.authenticators}
          fallback="Приложение"
          empty="Не подключено"
        />
        {codes && security.authenticators.length === 1 && (
          <p className="mt-2 text-sm text-muted-foreground">
            Удалите приложение — при входе вместо кода из него станут спрашивать коды
            восстановления. Чтобы выключить 2FA совсем, удалите и их.
          </p>
        )}
      </Section>

      <Section
        title="Коды восстановления"
        hint="Одноразовые коды на случай, если телефона с приложением нет под рукой. Храните их отдельно от телефона."
        action={
          // Коды — запасной путь для 2FA: без приложения-аутентификатора они стали бы 2FA сами по себе.
          hasApp && (
            <AccountActionLink action="configure-recovery-codes">
              {codes ? "Создать новые" : "Создать"}
            </AccountActionLink>
          )
        }
      >
        {codes && !hasApp && (
          <p role="alert" className="mb-2 text-sm text-destructive">
            Приложение не подключено, а коды остались: при входе их спрашивают вместо кода из
            приложения. Удалите коды или подключите приложение.
          </p>
        )}
        {codes ? (
          <div className="flex flex-wrap items-center gap-3 text-sm">
            <span>
              {codes.remaining !== null && codes.total !== null
                ? `Осталось ${codes.remaining} из ${codes.total}`
                : "Созданы"}
              <span className="text-muted-foreground"> · {formatDateTime(codes.createdAt)}</span>
            </span>
            <div className="ml-auto">
              <AccountActionLink action="delete-credential" credential={codes.id} destructive>
                Удалить
              </AccountActionLink>
            </div>
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">
            {hasApp ? "Не созданы" : "Сначала подключите приложение-аутентификатор"}
          </p>
        )}
      </Section>

      <Section
        title="Ключи доступа"
        hint="Вход без пароля — отпечатком пальца, лицом или PIN-кодом устройства."
        action={<AccountActionLink action="register-passkey">Добавить</AccountActionLink>}
      >
        <CredentialList items={security.passkeys} fallback="Ключ доступа" empty="Нет ключей" />
      </Section>
    </div>
  );
}

function Section({
  title,
  hint,
  action,
  children,
}: {
  title: string;
  hint: string;
  action: ReactNode;
  children?: ReactNode;
}) {
  return (
    <Card aria-label={title}>
      <div className="flex flex-wrap items-start gap-3">
        <div>
          <CardTitle className="mb-1">{title}</CardTitle>
          <p className="text-sm text-muted-foreground">{hint}</p>
        </div>
        <div className="ml-auto">{action}</div>
      </div>
      {children && <div className="mt-3">{children}</div>}
    </Card>
  );
}

/** Подключённое — подпись (или общее название), когда, «Удалить». */
function CredentialList({
  items,
  fallback,
  empty,
}: {
  items: AccountCredential[];
  fallback: string;
  empty: string;
}) {
  if (items.length === 0) {
    return <p className="text-sm text-muted-foreground">{empty}</p>;
  }

  return (
    <ul className="divide-y divide-border">
      {items.map((item) => (
        <li key={item.id} className="flex flex-wrap items-center gap-3 py-2 text-sm">
          <span className="font-medium">{item.label ?? fallback}</span>
          <span className="text-muted-foreground">{formatDateTime(item.createdAt)}</span>
          <div className="ml-auto">
            <AccountActionLink action="delete-credential" credential={item.id} destructive>
              Удалить
            </AccountActionLink>
          </div>
        </li>
      ))}
    </ul>
  );
}
