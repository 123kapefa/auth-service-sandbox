import { queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import {
  type CodeSettings,
  type CodeSettingsChanges,
  type CodeUsage,
  type MethodUsers,
  type ProjectMethod,
  type ProjectMethodState,
  type ProjectSignIn,
} from "./types";

export const signInSettingsApi = {
  codes: async () =>
    unwrap(await apiClient.get<Envelope<CodeSettings>>("/auth/admin/codes/settings")),
  saveCodes: async (changes: CodeSettingsChanges) =>
    unwrap(await apiClient.put<Envelope<CodeSettings>>("/auth/admin/codes/settings", changes)),
  usage: async () => unwrap(await apiClient.get<Envelope<CodeUsage>>("/auth/admin/codes/usage")),
  methods: async () =>
    unwrap(await apiClient.get<Envelope<ProjectSignIn>>("/auth/admin/sign-in/methods")),
  /** Способы и 2FA целиком; переходный период — status transition и until. */
  saveMethods: async (settings: ProjectSignIn) =>
    unwrap(
      await apiClient.put<Envelope<ProjectSignIn>>("/auth/admin/sign-in/methods", {
        methods: Object.fromEntries(
          Object.entries(settings.methods).map(([method, state]: [string, ProjectMethodState]) => [
            method,
            { status: state.status, until: state.status === "transition" ? state.until : null },
          ]),
        ),
        twoFactor: settings.twoFactor,
      }),
    ),
  affected: async (method: ProjectMethod) =>
    unwrap(
      await apiClient.get<Envelope<MethodUsers>>(`/auth/admin/sign-in/methods/${method}/affected`),
    ),
};

export const signInSettingsQueries = {
  all: () => ["sign-in-settings"] as const,
  codes: () =>
    queryOptions({
      queryKey: [...signInSettingsQueries.all(), "codes"],
      queryFn: signInSettingsApi.codes,
    }),
  usage: () =>
    queryOptions({
      queryKey: [...signInSettingsQueries.all(), "usage"],
      queryFn: signInSettingsApi.usage,
      refetchInterval: 60_000,
    }),
  methods: () =>
    queryOptions({
      queryKey: [...signInSettingsQueries.all(), "methods"],
      queryFn: signInSettingsApi.methods,
    }),
  affected: (method: ProjectMethod) =>
    queryOptions({
      queryKey: [...signInSettingsQueries.all(), "affected", method],
      queryFn: () => signInSettingsApi.affected(method),
      staleTime: 0,
    }),
};
