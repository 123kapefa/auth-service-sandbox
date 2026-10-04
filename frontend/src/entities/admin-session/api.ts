import { queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import { type AdminSession } from "./types";

export const adminSessionApi = {
  me: async () => unwrap(await apiClient.get<Envelope<AdminSession>>("/auth/admin/me")),
  logout: async () => {
    await apiClient.post("/auth/logout");
  },
  /** Режимы, между которыми можно переключиться (GET /me/): у сотрудника с ролью покупателя — и customer. */
  modes: async () =>
    unwrap(await apiClient.get<Envelope<{ availableModes: string[] }>>("/me/")).availableModes,
  /** Сменить активный режим: новые токены сервис сразу кладёт в cookie. */
  switchMode: async (activeMode: string) => {
    unwrap(await apiClient.post<Envelope<unknown>>("/auth/mode", { activeMode }));
  },
};

export const adminSessionQueries = {
  me: () => queryOptions({ queryKey: ["admin", "me"], queryFn: adminSessionApi.me }),
  modes: () => queryOptions({ queryKey: ["admin", "modes"], queryFn: adminSessionApi.modes }),
};
