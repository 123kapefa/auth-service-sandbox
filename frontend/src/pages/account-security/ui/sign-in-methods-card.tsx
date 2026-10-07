import { useState, type ReactNode } from "react";
import { type AccountProfile } from "@/entities/account-profile";
import {
  type AccountSecurity,
  type SignInMethod,
  type SignInMethodKey,
  type SignInMethods,
} from "@/entities/account-security";
import { AccountActionLink } from "@/features/account-action";
import { formatDateTime, formatPhone } from "@/shared/lib/format";
import { Badge, Button, Card, CardTitle } from "@/shared/ui";
import {
  DisableMethodDialog,
  keysCount,
  methodTitle,
  projectMethod,
  providerTitle,
  type RemainingWay,
  useSetSignInMethod,
} from "@/features/account-sign-in-methods";

/**
 * «Способы входа» (экраны 01, 04, 08 этапа 10): пароль, код на телефон, код на почту — включить и выключить (выключение
 * — с окном), ключ доступа и привязанные Яндекс ID и Госуслуги. Выключенное в проекте не показываем. Пароль и код на
 * телефон без ключа доступа или приложения-аутентификатора не выключить — объясняем и даём добавить.
 */
export function SignInMethodsCard({
  methods,
  security,
  profile,
}: {
  methods: SignInMethods;
  security: AccountSecurity;
  profile: AccountProfile;
}) {
  const [disabling, setDisabling] = useState<SignInMethodKey | null>(null);
  const enable = useSetSignInMethod();
  const offInProject = new Set(methods.projectDisabled);
  const own = methods.methods.filter((item) => !offInProject.has(projectMethod[item.method]));
  const passkeyShown = !offInProject.has("passkey");
  const providers = methods.linkedProviders.filter((provider) => !offInProject.has(provider));
  // Без ключа и аутентификатора пароль и код на телефон не выключить — объяснение под ними (экран 04).
  const needingAnchor = own.filter(
    (item) => item.enabled && item.disableBlocker === "needs_passkey_or_authenticator",
  );
  const anchorAfter = needingAnchor.at(-1)?.method;

  const target = (method: SignInMethodKey) =>
    method === "phone-code" ? profile.phone : method === "email-code" ? profile.email : null;

  const remaining = (except: SignInMethodKey): RemainingWay[] => [
    ...(security.passkeys.length > 0 && passkeyShown
      ? [
          {
            title: "Ключ доступа",
            detail: security.passkeys.map((key) => key.label ?? "Ключ доступа").join(", "),
          },
        ]
      : []),
    ...own
      .filter((item) => item.method !== except && item.available && item.enabled)
      .map((item) => ({
        title: methodTitle[item.method],
        detail: shownTarget(target(item.method)),
      })),
    ...providers.map((provider) => ({ title: providerTitle[provider] ?? provider, detail: null })),
  ];

  return (
    <Card aria-label="Способы входа">
      <CardTitle className="mb-1">Способы входа</CardTitle>
      <p className="text-sm text-muted-foreground">
        Как можно войти в аккаунт. Выключенный способ не сработает ни у кого — даже с верным паролем
        или кодом.
      </p>
      <ul className="mt-4 divide-y divide-border border-t border-border">
        {own.map((item) => (
          <li key={item.method}>
            <Row
              title={methodTitle[item.method]}
              badges={<MethodBadge item={item} />}
              hint={methodHint(item, target(item.method), methods)}
              actions={
                <>
                  {item.method === "password" && (
                    <AccountActionLink action="update-password">
                      {item.available ? "Сменить" : "Задать"}
                    </AccountActionLink>
                  )}
                  {(item.available || item.method !== "password") && (
                    <ToggleButton
                      item={item}
                      pending={enable.isPending}
                      onDisable={() => setDisabling(item.method)}
                      onEnable={() => enable.mutate({ method: item.method, enabled: true })}
                    />
                  )}
                </>
              }
            />
            {anchorAfter === item.method && <AnchorNotice passkeyShown={passkeyShown} />}
          </li>
        ))}
        {passkeyShown && (
          <li>
            <Row
              title="Ключ доступа"
              badges={
                <>
                  {security.passkeys.length > 0 ? (
                    <Badge variant="solid">{keysCount(security.passkeys.length)}</Badge>
                  ) : (
                    <Badge variant="warning">нет</Badge>
                  )}
                  <Badge className="text-muted-foreground uppercase">надёжный</Badge>
                </>
              }
              hint={
                security.passkeys.length > 0
                  ? `${security.passkeys.map((key) => key.label ?? "Ключ доступа").join(", ")} — список ниже`
                  : "Face ID, отпечаток или PIN устройства"
              }
              actions={<AccountActionLink action="register-passkey">Добавить</AccountActionLink>}
            />
          </li>
        )}
        {providers.map((provider) => (
          <li key={provider}>
            <Row
              title={providerTitle[provider] ?? provider}
              badges={<Badge variant="solid">привязан</Badge>}
              hint="Вход в один шаг на странице входа"
              actions={null}
            />
          </li>
        ))}
      </ul>
      {disabling && (
        <DisableMethodDialog
          method={disabling}
          remaining={remaining(disabling)}
          onClose={() => setDisabling(null)}
        />
      )}
    </Card>
  );
}

function Row({
  title,
  badges,
  hint,
  actions,
}: {
  title: string;
  badges: ReactNode;
  hint: ReactNode;
  actions: ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-center gap-3 py-4">
      <div className="min-w-0 flex-[1_1_14rem]">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-semibold">{title}</span>
          {badges}
        </div>
        <p className="mt-0.5 text-sm text-muted-foreground">{hint}</p>
      </div>
      {actions && <div className="ml-auto flex flex-wrap gap-2">{actions}</div>}
    </div>
  );
}

function MethodBadge({ item }: { item: SignInMethod }) {
  if (!item.available) {
    const missing: Record<SignInMethodKey, string> = {
      password: "не задан",
      "phone-code": "нет номера",
      "email-code": "нет почты",
    };
    return <Badge variant="warning">{missing[item.method]}</Badge>;
  }

  return item.enabled ? (
    <Badge variant="solid">включён</Badge>
  ) : (
    <Badge variant="warning">выключен</Badge>
  );
}

function methodHint(item: SignInMethod, target: string | null, methods: SignInMethods): ReactNode {
  if (!item.enabled) {
    // Когда выключили — последняя запись о выключении этого способа.
    const since = methods.history.find(
      (change) => change.method === item.method && change.enabled === false,
    );
    const when = since ? `с ${formatDateTime(since.at)} · ` : "";
    return item.method === "password"
      ? `${when}«Забыли пароль» тоже не работает`
      : `${when}код не придёт никому`;
  }

  if (!item.available) {
    return item.method === "password"
      ? "Пароль не задан: входите по коду или ключом доступа"
      : item.method === "phone-code"
        ? "Добавьте номер в профиле"
        : "Добавьте почту в профиле";
  }

  const blocker =
    item.disableBlocker === "last_method"
      ? " · последний способ входа — не выключить"
      : item.disableBlocker === "recovery_codes_required"
        ? " · чтобы выключить, сначала создайте коды восстановления"
        : "";
  switch (item.method) {
    case "password":
      return `Задан. Вводится на странице входа — сервис его не видит${blocker}`;
    case "phone-code":
      return `${shownTarget(target)} · звонком, если не дошёл — SMS${blocker}`;
    case "email-code":
      return `на ${shownTarget(target)}${blocker}`;
  }
}

function ToggleButton({
  item,
  pending,
  onDisable,
  onEnable,
}: {
  item: SignInMethod;
  pending: boolean;
  onDisable: () => void;
  onEnable: () => void;
}) {
  if (!item.available) {
    return (
      <Button size="sm" variant="outline" disabled>
        Включить
      </Button>
    );
  }

  return item.enabled ? (
    <Button
      size="sm"
      variant="outline"
      disabled={item.disableBlocker !== null}
      aria-label={`Выключить: ${methodTitle[item.method]}`}
      onClick={onDisable}
    >
      Выключить
    </Button>
  ) : (
    <Button
      size="sm"
      variant="outline"
      disabled={pending}
      aria-label={`Включить: ${methodTitle[item.method]}`}
      onClick={onEnable}
    >
      Включить
    </Button>
  );
}

/** Экран 04: пароль и код на телефон выключаются, только когда есть ключ доступа или приложение-аутентификатор. */
function AnchorNotice({ passkeyShown }: { passkeyShown: boolean }) {
  return (
    <div className="mb-4 border border-dashed border-primary bg-hatch p-4 text-sm">
      <p>
        <strong>Сначала добавьте надёжный способ.</strong> Пароль и код на телефон можно выключить,
        когда есть ключ доступа или приложение-аутентификатор. Иначе вход будет держаться на одном
        слабом способе — например, только на номере телефона, а номер могут перевыпустить или
        передать другому человеку.
      </p>
      <div className="mt-3 flex flex-wrap gap-2">
        {passkeyShown && (
          <AccountActionLink action="register-passkey" primary>
            Добавить ключ доступа
          </AccountActionLink>
        )}
        <AccountActionLink action="configure-totp">Подключить приложение</AccountActionLink>
      </div>
    </div>
  );
}

function shownTarget(target: string | null): string {
  if (!target) {
    return "—";
  }

  return target.includes("@") ? target : formatPhone(target);
}
