import { queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import { type AccountProfile } from "./types";

export const accountProfileApi = {
  get: async () => unwrap(await apiClient.get<Envelope<AccountProfile>>("/me/profile")),
  logout: async () => {
    await apiClient.post("/auth/logout");
  },
};

export const accountProfileQueries = {
  profile: () => queryOptions({ queryKey: ["account", "profile"], queryFn: accountProfileApi.get }),
};
