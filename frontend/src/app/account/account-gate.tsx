import { useSuspenseQuery } from "@tanstack/react-query";
import { accountProfileQueries } from "@/entities/account-profile";
import { AccountShell } from "@/widgets/account-shell";

/** Кабинет — любому вошедшему: без сессии клиент API уже увёл на вход (401), иначе каркас с разделами. */
export function AccountGate() {
  const { data: profile } = useSuspenseQuery(accountProfileQueries.profile());
  return <AccountShell profile={profile} />;
}
