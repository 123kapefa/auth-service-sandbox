import { useMutation, useQueryClient } from "@tanstack/react-query";
import { userApi, userQueries } from "@/entities/user";
import { notifyDone, notifyError } from "@/shared/lib/notify";

/** Вернуть способы входа по умолчанию (POST /auth/admin/users/{id}/sign-in-methods/reset) — после недавнего входа. */
export function useResetSignInMethods(userId: string, onDone: () => void) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => userApi.resetSignInMethods(userId),
    onSuccess: async (methods) => {
      queryClient.setQueryData(userQueries.signInMethods(userId).queryKey, methods);
      await queryClient.invalidateQueries({ queryKey: userQueries.detail(userId).queryKey });
      notifyDone("Способы входа возвращены по умолчанию");
      onDone();
    },
    onError: (error) => notifyError("вернуть способы по умолчанию", error),
  });
}
