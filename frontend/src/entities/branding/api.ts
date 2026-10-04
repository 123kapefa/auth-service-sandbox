import { queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import { type AppBranding, type AppBrandingOwn, type Branding } from "./types";

const BASE = "/auth/admin/branding";
const appPath = (clientId: string) => `/auth/admin/apps/${encodeURIComponent(clientId)}/branding`;

export const brandingApi = {
  get: async () => unwrap(await apiClient.get<Envelope<Branding>>(BASE)),
  save: async (branding: Branding) =>
    unwrap(await apiClient.put<Envelope<Branding>>(BASE, branding)),
  getApp: async (clientId: string) =>
    unwrap(await apiClient.get<Envelope<AppBranding>>(appPath(clientId))),
  /** Для кабинета: проект, поверх — приложение, из которого кабинет открыт. */
  mine: async (app: string | null) =>
    unwrap(
      await apiClient.get<Envelope<Branding>>("/me/branding", {
        params: app ? { app } : undefined,
      }),
    ),
  saveApp: async (clientId: string, branding: AppBrandingOwn) =>
    unwrap(await apiClient.put<Envelope<AppBranding>>(appPath(clientId), branding)),
};

export const brandingQueries = {
  all: () => ["branding"] as const,
  current: () =>
    queryOptions({
      queryKey: [...brandingQueries.all(), "current"],
      queryFn: brandingApi.get,
    }),
  mine: (app: string | null) =>
    queryOptions({
      queryKey: [...brandingQueries.all(), "mine", app],
      queryFn: () => brandingApi.mine(app),
      staleTime: Infinity,
    }),
  app: (clientId: string) =>
    queryOptions({
      queryKey: [...brandingQueries.all(), "app", clientId],
      queryFn: () => brandingApi.getApp(clientId),
    }),
};
