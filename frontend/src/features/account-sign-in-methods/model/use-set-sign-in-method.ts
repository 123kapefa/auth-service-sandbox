import { useMutation, useQueryClient } from "@tanstack/react-query";
import { accountDevicesQueries } from "@/entities/account-devices";
import { accountSecurityApi, accountSecurityQueries } from "@/entities/account-security";
import { notifyDone, notifyError } from "@/shared/lib/notify";
import { methodTitle } from "./labels";

/**
 * Выключить или включить свой способ входа. Вход был давно — уведомление с «Войти заново» (notifyError): после входа
 * человек нажимает ещё раз. Выключили с «выйти на других» — список устройств устарел.
 */
export function useSetSignInMethod(onDone?: () => void) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: accountSecurityApi.setSignInMethod,
    onSuccess: async (methods, { method, enabled }) => {
      queryClient.setQueryData(accountSecurityQueries.signInMethods().queryKey, methods);
      await queryClient.invalidateQueries({ queryKey: accountDevicesQueries.all() });
      notifyDone(`${methodTitle[method]}: ${enabled ? "включён" : "выключен"}`);
      onDone?.();
    },
    onError: (error, { enabled }) =>
      notifyError(enabled ? "включить способ входа" : "выключить способ входа", error),
  });
}
