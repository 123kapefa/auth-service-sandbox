import { queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import { type AppPermission, type AppRole, type RoleDraft } from "./types";

const base = (clientId: string) => `/auth/admin/apps/${clientId}`;

export const appCatalogApi = {
  roles: async (clientId: string) =>
    unwrap(await apiClient.get<Envelope<AppRole[]>>(`${base(clientId)}/roles`)),
  permissions: async (clientId: string) =>
    unwrap(await apiClient.get<Envelope<AppPermission[]>>(`${base(clientId)}/permissions`)),
  createRole: async (clientId: string, role: RoleDraft) =>
    unwrap(await apiClient.post<Envelope<AppRole[]>>(`${base(clientId)}/roles`, role)),
  updateRole: async (clientId: string, role: RoleDraft) =>
    unwrap(
      await apiClient.put<Envelope<AppRole[]>>(`${base(clientId)}/roles/${role.name}`, {
        description: role.description,
        permissions: role.permissions,
      }),
    ),
  deleteRole: async (clientId: string, name: string) =>
    unwrap(await apiClient.delete<Envelope<AppRole[]>>(`${base(clientId)}/roles/${name}`)),
  createPermission: async (clientId: string, permission: { name: string; description: string }) =>
    unwrap(
      await apiClient.post<Envelope<AppPermission[]>>(`${base(clientId)}/permissions`, permission),
    ),
  deletePermission: async (clientId: string, name: string) =>
    unwrap(
      await apiClient.delete<Envelope<AppPermission[]>>(`${base(clientId)}/permissions/${name}`),
    ),
};

export const appCatalogQueries = {
  all: (clientId: string) => ["apps", clientId, "catalog"] as const,
  roles: (clientId: string) =>
    queryOptions({
      queryKey: [...appCatalogQueries.all(clientId), "roles"],
      queryFn: () => appCatalogApi.roles(clientId),
    }),
  permissions: (clientId: string) =>
    queryOptions({
      queryKey: [...appCatalogQueries.all(clientId), "permissions"],
      queryFn: () => appCatalogApi.permissions(clientId),
    }),
};
