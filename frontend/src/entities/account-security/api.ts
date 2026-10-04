import { queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import { type AccountSecurity } from "./types";

export const accountSecurityApi = {
  get: async () => unwrap(await apiClient.get<Envelope<AccountSecurity>>("/me/security")),
};

export const accountSecurityQueries = {
  security: () =>
    queryOptions({ queryKey: ["account", "security"], queryFn: accountSecurityApi.get }),
};
