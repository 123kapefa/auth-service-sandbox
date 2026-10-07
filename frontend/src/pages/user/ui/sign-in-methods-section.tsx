import { useQuery } from "@tanstack/react-query";
import { type ReactNode } from "react";
import { type SignInMethod, type SignInMethods } from "@/entities/account-security";
import { userQueries } from "@/entities/user";
import { ResetSignInMethods } from "@/features/user-sign-in-reset";
import { getErrorMessage } from "@/shared/api";
import { formatDateTime } from "@/shared/lib/format";
import { Badge, Card, CardTitle, ErrorState, Loading } from "@/shared/ui";

const titles: Record<SignInMethod["method"], string> = {
  password: "Пароль",
  "phone-code": "Код на телефон",
  "email-code": "Код на почту",
};

const providers: Record<string, string> = { yandex: "Яндекс ID", esia: "Госуслуги" };

/**
 * «Способы входа» в карточке пользователя (экран 07 этапа 10): что человек выключил сам в кабинете. Админ видит, но не
 * меняет — только возвращает всё по умолчанию (canReset — users.credentials.manage; себе — нельзя).
 */
export function SignInMethodsSection({
  userId,
  canReset,
  self,
}: {
  userId: string;
  canReset: boolean;
  self: boolean;
}) {
  const query = useQuery(userQueries.signInMethods(userId));

  return (
    <Card aria-label="Способы входа">
      <CardTitle className="mb-1">Способы входа</CardTitle>
      <p className="mb-4 text-sm text-muted-foreground">
        Выбирает сам человек в кабинете. Админ видит, но не меняет — только возвращает всё по
        умолчанию.
      </p>
      {query.isPending ? (
        <Loading />
      ) : query.isError ? (
        <ErrorState message={getErrorMessage(query.error, "Не удалось загрузить способы входа")} />
      ) : (
        <>
          <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-[13rem_minmax(0,1fr)]">
            {query.data.methods.map((item) => (
              <Line key={item.method} title={titles[item.method]}>
                <MethodState item={item} methods={query.data} />
              </Line>
            ))}
            <Line title="Ключи доступа">{query.data.hasPasskey ? "есть" : "нет"}</Line>
            <Line title="2FA">{query.data.hasAuthenticator ? "подключена" : "не подключена"}</Line>
            <Line title="Коды восстановления">{query.data.hasRecoveryCodes ? "есть" : "нет"}</Line>
            {query.data.linkedProviders.length > 0 && (
              <Line title="Внешний вход">
                {query.data.linkedProviders.map((name) => providers[name] ?? name).join(", ")}
              </Line>
            )}
          </dl>
          {canReset && (
            <ResetSignInMethods
              userId={userId}
              blocked={
                self
                  ? "Свои способы входа меняйте в кабинете."
                  : query.data.methods.every((item) => item.enabled)
                    ? "Всё и так включено."
                    : null
              }
            />
          )}
        </>
      )}
    </Card>
  );
}

function Line({ title, children }: { title: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-muted-foreground">{title}</dt>
      <dd className="flex flex-wrap items-center gap-2">{children}</dd>
    </>
  );
}

function MethodState({ item, methods }: { item: SignInMethod; methods: SignInMethods }) {
  if (!item.available) {
    const missing = { password: "не задан", "phone-code": "нет номера", "email-code": "нет почты" };
    return <Badge variant="warning">{missing[item.method]}</Badge>;
  }

  if (item.enabled) {
    return <Badge variant="solid">включён</Badge>;
  }

  const since = methods.history.find(
    (change) => change.method === item.method && change.enabled === false,
  );
  return (
    <>
      <Badge variant="warning">выключен</Badge>
      {since && (
        <span>
          {since.byAdmin ? "админом" : "человеком"}, {formatDateTime(since.at)}
        </span>
      )}
    </>
  );
}
