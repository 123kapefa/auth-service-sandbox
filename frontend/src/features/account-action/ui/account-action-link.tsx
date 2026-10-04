import { type ReactNode } from "react";
import { ACCOUNT_BASE, type AccountAction, accountActionUrl } from "@/shared/config/routes";
import { Button } from "@/shared/ui";
import { ACTION_PARAM } from "../model/status";

/**
 * Кнопка действия с учётной записью: уход на страницу сервера входа (тема проекта) и возврат на текущую страницу
 * кабинета. Обычная ссылка — пароль и почта вводятся там, страница кабинета их не видит.
 */
export function AccountActionLink({
  action,
  children,
}: {
  action: AccountAction;
  children: ReactNode;
}) {
  const here = new URL(window.location.href);
  const path = here.pathname.startsWith(ACCOUNT_BASE) ? here.pathname : `${ACCOUNT_BASE}/`;
  const returnTo = `${path}?${new URLSearchParams({ [ACTION_PARAM]: action }).toString()}`;

  return (
    <Button asChild variant="outline" size="sm">
      <a href={accountActionUrl(action, returnTo)}>{children}</a>
    </Button>
  );
}
