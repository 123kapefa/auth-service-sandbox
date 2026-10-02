import { useSuspenseQuery } from "@tanstack/react-query";
import { adminSessionQueries } from "./api";
import { type ManagedApp } from "./types";

/** Приложение под управлением по clientId (из адреса) или null — не своё. */
export function useManagedApp(clientId: string): ManagedApp | null {
  const { data: session } = useSuspenseQuery(adminSessionQueries.me());
  return session.apps.find((app) => app.clientId === clientId) ?? null;
}
