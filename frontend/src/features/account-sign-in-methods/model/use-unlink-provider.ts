import { useMutation, useQueryClient } from "@tanstack/react-query";
import { accountDevicesQueries } from "@/entities/account-devices";
import { accountSecurityApi, accountSecurityQueries } from "@/entities/account-security";
import { notifyDone, notifyError } from "@/shared/lib/notify";
import { providerTitle } from "./labels";

/**
 * Отвязать Яндекс ID или Госуслуги. Вход был давно — уведомление с «Войти заново» (notifyError): после входа человек
 * нажимает ещё раз. С «выйти на других» список устройств устарел.
 */
export function useUnlinkProvider(onDone?: () => void) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: accountSecurityApi.unlinkProvider,
    onSuccess: async (methods, { provider }) => {
      queryClient.setQueryData(accountSecurityQueries.signInMethods().queryKey, methods);
      await queryClient.invalidateQueries({ queryKey: accountDevicesQueries.all() });
      notifyDone(`${providerTitle[provider] ?? provider}: отвязан`);
      onDone?.();
    },
    onError: (error) => notifyError("отвязать вход", error),
  });
}
