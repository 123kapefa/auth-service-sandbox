import { queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import { type Branding } from "./types";

const BASE = "/auth/admin/branding";

export const brandingApi = {
  get: async () => unwrap(await apiClient.get<Envelope<Branding>>(BASE)),
  save: async (branding: Branding) =>
    unwrap(await apiClient.put<Envelope<Branding>>(BASE, branding)),
};

export const brandingQueries = {
  all: () => ["branding"] as const,
  current: () =>
    queryOptions({
      queryKey: [...brandingQueries.all(), "current"],
      queryFn: brandingApi.get,
    }),
};
