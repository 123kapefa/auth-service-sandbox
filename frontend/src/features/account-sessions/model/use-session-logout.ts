import { useMutation, useQueryClient } from "@tanstack/react-query";
import { accountDevicesApi, accountDevicesQueries } from "@/entities/account-devices";
import { accountProfileApi } from "@/entities/account-profile";
import { notifyDone, notifyError } from "@/shared/lib/notify";

/** Выйти на одном своём устройстве (не текущем). */
export function useTerminateSession() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: accountDevicesApi.terminate,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: accountDevicesQueries.all() });
      notifyDone("Сессия завершена");
    },
    onError: (error) => notifyError("выйти на устройстве", error),
  });
}

/** Выйти на всех других устройствах — эта сессия остаётся. */
export function useLogoutOthers() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: accountDevicesApi.terminateOthers,
    onSuccess: async (terminated) => {
      await queryClient.invalidateQueries({ queryKey: accountDevicesQueries.all() });
      notifyDone(terminated > 0 ? `Завершено сессий: ${terminated}` : "Других сессий нет");
    },
    onError: (error) => notifyError("выйти на других устройствах", error),
  });
}

/** Выйти везде: сначала все другие сессии, потом эта (как кнопка «Выйти») — и на главную, там вход. */
export function useLogoutEverywhere() {
  return useMutation({
    mutationFn: async () => {
      await accountDevicesApi.terminateOthers();
      await accountProfileApi.logout();
    },
    onSuccess: () => window.location.assign("/"),
    onError: (error) => notifyError("выйти везде", error),
  });
}
