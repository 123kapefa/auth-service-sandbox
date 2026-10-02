import { queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import { type AdminSession } from "./types";

export const adminSessionApi = {
  me: async () => unwrap(await apiClient.get<Envelope<AdminSession>>("/auth/admin/me")),
  logout: async () => {
    await apiClient.post("/auth/logout");
  },
};

export const adminSessionQueries = {
  me: () => queryOptions({ queryKey: ["admin", "me"], queryFn: adminSessionApi.me }),
};
