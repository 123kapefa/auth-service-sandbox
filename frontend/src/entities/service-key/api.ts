import { queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import { type IssuedServiceKey, type ServiceKey, type ServiceKeyDraft } from "./types";

const BASE = "/auth/admin/services";

export const serviceKeyApi = {
  list: async () => unwrap(await apiClient.get<Envelope<ServiceKey[]>>(BASE)),
  issue: async (draft: ServiceKeyDraft) =>
    unwrap(await apiClient.post<Envelope<IssuedServiceKey>>(BASE, draft)),
  rotate: async (code: string) =>
    unwrap(await apiClient.post<Envelope<IssuedServiceKey>>(`${BASE}/${code}/rotate`)),
  setPermissions: async (code: string, permissions: string[]) =>
    unwrap(
      await apiClient.put<Envelope<ServiceKey>>(`${BASE}/${code}/permissions`, { permissions }),
    ),
  revoke: async (code: string) => unwrap(await apiClient.delete<Envelope<null>>(`${BASE}/${code}`)),
};

export const serviceKeyQueries = {
  all: () => ["services"] as const,
  list: () =>
    queryOptions({
      queryKey: [...serviceKeyQueries.all(), "list"],
      queryFn: serviceKeyApi.list,
    }),
};
