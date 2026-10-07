import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  type ProjectSignIn,
  signInSettingsApi,
  signInSettingsQueries,
} from "@/entities/sign-in-settings";
import { notifyDone, notifyError } from "@/shared/lib/notify";

/** Сохранить способы входа проекта и 2FA целиком (PUT /auth/admin/sign-in/methods). */
export function useSaveProjectSignIn(onDone?: () => void) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ settings }: { settings: ProjectSignIn; done: string }) =>
      signInSettingsApi.saveMethods(settings),
    onSuccess: async (saved, { done }) => {
      queryClient.setQueryData(signInSettingsQueries.methods().queryKey, saved);
      await queryClient.invalidateQueries({ queryKey: signInSettingsQueries.all() });
      notifyDone(done);
      onDone?.();
    },
    onError: (error) => notifyError("изменение способов входа", error),
  });
}
