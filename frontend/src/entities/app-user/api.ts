import { keepPreviousData, queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import { type AppUsersPage, type UserAppRoles } from "./types";

export const appUserApi = {
  list: async (clientId: string, search: string, page: number) =>
    unwrap(
      await apiClient.get<Envelope<AppUsersPage>>(`/auth/admin/apps/${clientId}/users`, {
        params: { search: search || undefined, page, pageSize: 20 },
      }),
    ),
  roles: async (clientId: string, userId: string) =>
    unwrap(
      await apiClient.get<Envelope<UserAppRoles>>(
        `/auth/admin/apps/${clientId}/users/${userId}/roles`,
      ),
    ),
  setRoles: async (clientId: string, userId: string, roles: string[]) =>
    unwrap(
      await apiClient.put<Envelope<UserAppRoles>>(
        `/auth/admin/apps/${clientId}/users/${userId}/roles`,
        { roles },
      ),
    ),
};

export const appUserQueries = {
  all: (clientId: string) => ["apps", clientId, "users"] as const,
  list: (clientId: string, search: string, page: number) =>
    queryOptions({
      queryKey: [...appUserQueries.all(clientId), "list", search, page],
      queryFn: () => appUserApi.list(clientId, search, page),
      placeholderData: keepPreviousData,
    }),
  roles: (clientId: string, userId: string) =>
    queryOptions({
      queryKey: [...appUserQueries.all(clientId), userId, "roles"],
      queryFn: () => appUserApi.roles(clientId, userId),
    }),
};
