import { queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import { type AccountSession, type LoginAttempt } from "./types";

export const accountDevicesApi = {
  sessions: async () =>
    unwrap(await apiClient.get<Envelope<{ items: AccountSession[] }>>("/security/sessions")).items,
  logins: async () =>
    unwrap(await apiClient.get<Envelope<{ items: LoginAttempt[] }>>("/security/logins")).items,
  /** Завершить свою сессию на другом устройстве: её токены отзываются сразу. */
  terminate: async (sessionId: string) => {
    await apiClient.post(`/security/sessions/${encodeURIComponent(sessionId)}/terminate`);
  },
  /** Завершить все свои сессии, кроме текущей. */
  terminateOthers: async () =>
    unwrap(
      await apiClient.post<Envelope<{ terminated: number }>>(
        "/security/sessions/terminate-all-others",
      ),
    ).terminated,
};

export const accountDevicesQueries = {
  all: () => ["account", "devices"] as const,
  sessions: () =>
    queryOptions({
      queryKey: [...accountDevicesQueries.all(), "sessions"],
      queryFn: accountDevicesApi.sessions,
    }),
  logins: () =>
    queryOptions({
      queryKey: [...accountDevicesQueries.all(), "logins"],
      queryFn: accountDevicesApi.logins,
    }),
};
