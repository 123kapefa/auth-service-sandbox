import { useMutation, useQueryClient } from "@tanstack/react-query";
import { appModeApi, appModeQueries } from "@/entities/app-mode";
import { notifyDone, notifyError } from "@/shared/lib/notify";

/** Переключить режим в приложении проекта. */
export function useSwitchMode() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: appModeApi.switch,
    onSuccess: async (_result, { activeMode }) => {
      await queryClient.invalidateQueries({ queryKey: appModeQueries.all() });
      notifyDone(`Режим: ${activeMode}`);
    },
    onError: (error) => notifyError("переключить режим", error),
  });
}
