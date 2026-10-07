import { queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import { type AccountSecurity, type SignInMethodKey, type SignInMethods } from "./types";

export const accountSecurityApi = {
  get: async () => unwrap(await apiClient.get<Envelope<AccountSecurity>>("/me/security")),
  signInMethods: async () =>
    unwrap(await apiClient.get<Envelope<SignInMethods>>("/me/sign-in-methods")),
  /** Выключить или включить способ входа; только после недавнего входа (иначе auth.reauthentication_required). */
  setSignInMethod: async ({
    method,
    enabled,
    signOutOtherDevices,
  }: {
    method: SignInMethodKey;
    enabled: boolean;
    signOutOtherDevices?: boolean;
  }) =>
    unwrap(
      await apiClient.put<Envelope<SignInMethods>>(
        `/me/sign-in-methods/${encodeURIComponent(method)}`,
        { enabled, signOutOtherDevices },
      ),
    ),
};

export const accountSecurityQueries = {
  security: () =>
    queryOptions({ queryKey: ["account", "security"], queryFn: accountSecurityApi.get }),
  signInMethods: () =>
    queryOptions({
      queryKey: ["account", "sign-in-methods"],
      queryFn: accountSecurityApi.signInMethods,
    }),
};
