import { queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import { type AppMode } from "./types";

export const appModeApi = {
  list: async () => unwrap(await apiClient.get<Envelope<AppMode[]>>("/auth/modes")),
  /** Режим в приложении: токены приложения со старым режимом сразу перестают приниматься — оно обновит их само. */
  switch: async ({ app, activeMode }: { app: string; activeMode: string }) =>
    unwrap(await apiClient.post<Envelope<unknown>>("/auth/mode", { app, activeMode })),
};

export const appModeQueries = {
  all: () => ["account", "modes"] as const,
  list: () => queryOptions({ queryKey: appModeQueries.all(), queryFn: appModeApi.list }),
};
