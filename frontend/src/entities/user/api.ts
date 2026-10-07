import { keepPreviousData, queryOptions } from "@tanstack/react-query";
import { type SignInMethods } from "@/entities/account-security";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import { type AppAccess, type UserDetail, type UserSession, type UsersPage } from "./types";

export interface UsersSearch {
  search: string;
  page: number;
}

export const userApi = {
  list: async ({ search, page }: UsersSearch) =>
    unwrap(
      await apiClient.get<Envelope<UsersPage>>("/auth/admin/users", {
        params: { search: search || undefined, page, pageSize: 20 },
      }),
    ),
  detail: async (userId: string) =>
    unwrap(await apiClient.get<Envelope<UserDetail>>(`/auth/admin/users/${userId}`)),
  apps: async (userId: string) =>
    unwrap(await apiClient.get<Envelope<AppAccess[]>>(`/auth/admin/users/${userId}/apps`)),
  sessions: async (userId: string) =>
    unwrap(
      await apiClient.get<Envelope<{ items: UserSession[] }>>(
        `/auth/admin/users/${userId}/sessions`,
      ),
    ).items,
  /** Способы входа человека — как в кабинете (/me/sign-in-methods). */
  signInMethods: async (userId: string) =>
    unwrap(
      await apiClient.get<Envelope<SignInMethods>>(`/auth/admin/users/${userId}/sign-in-methods`),
    ),
  /** Вернуть способы по умолчанию: всё включено, человеку — письмо. */
  resetSignInMethods: async (userId: string) =>
    unwrap(
      await apiClient.post<Envelope<SignInMethods>>(
        `/auth/admin/users/${userId}/sign-in-methods/reset`,
      ),
    ),
};

export const userQueries = {
  all: () => ["users"] as const,
  list: (search: UsersSearch) =>
    queryOptions({
      queryKey: [...userQueries.all(), "list", search],
      queryFn: () => userApi.list(search),
      placeholderData: keepPreviousData,
    }),
  detail: (userId: string) =>
    queryOptions({
      queryKey: [...userQueries.all(), userId],
      queryFn: () => userApi.detail(userId),
    }),
  apps: (userId: string) =>
    queryOptions({
      queryKey: [...userQueries.all(), userId, "apps"],
      queryFn: () => userApi.apps(userId),
    }),
  sessions: (userId: string) =>
    queryOptions({
      queryKey: [...userQueries.all(), userId, "sessions"],
      queryFn: () => userApi.sessions(userId),
    }),
  signInMethods: (userId: string) =>
    queryOptions({
      queryKey: [...userQueries.all(), userId, "sign-in-methods"],
      queryFn: () => userApi.signInMethods(userId),
    }),
};
