import { type ReactNode } from "react";
import { ACCOUNT_BASE, type AccountAction, accountActionUrl } from "@/shared/config/routes";
import { Button } from "@/shared/ui";
import { ACTION_PARAM } from "../model/status";

/**
 * Кнопка действия с учётной записью: уход на страницу сервера входа (тема проекта) и возврат на текущую страницу
 * кабинета. Обычная ссылка — пароль, почта, коды и ключи вводятся и подтверждаются там, страница кабинета их не видит.
 * credential — что удалить (delete-credential): подтверждение спросит сам сервер входа. primary — основная кнопка.
 */
export function AccountActionLink({
  action,
  credential,
  destructive = false,
  primary = false,
  children,
}: {
  action: AccountAction;
  credential?: string;
  destructive?: boolean;
  primary?: boolean;
  children: ReactNode;
}) {
  const here = new URL(window.location.href);
  const path = here.pathname.startsWith(ACCOUNT_BASE) ? here.pathname : `${ACCOUNT_BASE}/`;
  const returnTo = `${path}?${new URLSearchParams({ [ACTION_PARAM]: action }).toString()}`;

  return (
    <Button
      asChild
      variant={destructive ? "destructive" : primary ? "default" : "outline"}
      size="sm"
    >
      <a href={accountActionUrl(action, returnTo, credential)}>{children}</a>
    </Button>
  );
}
