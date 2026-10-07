import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  type CodeSettingsChanges,
  signInSettingsApi,
  signInSettingsQueries,
} from "@/entities/sign-in-settings";
import { notifyDone, notifyError } from "@/shared/lib/notify";

/** Сохранить каналы, бюджет и лимиты кодов (PUT /auth/admin/codes/settings): плагин применит со следующего кода. */
export function useSaveCodeSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (changes: CodeSettingsChanges) => signInSettingsApi.saveCodes(changes),
    onSuccess: async (saved) => {
      queryClient.setQueryData(signInSettingsQueries.codes().queryKey, saved);
      await queryClient.invalidateQueries({ queryKey: signInSettingsQueries.usage().queryKey });
      notifyDone("Настройки кодов сохранены");
    },
    onError: (error) => notifyError("сохранение настроек кодов", error),
  });
}
