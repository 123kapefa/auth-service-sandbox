import { useMutation, useQueryClient } from "@tanstack/react-query";
import { userQueries } from "@/entities/user";
import { apiClient } from "@/shared/api";
import { notifyDone, notifyError } from "@/shared/lib/notify";

/** Письмо со ссылкой на смену пароля (POST /auth/admin/users/{id}/password-reset). */
export function useSendPasswordReset(userId: string) {
  return useMutation({
    mutationFn: () => apiClient.post(`/auth/admin/users/${userId}/password-reset`),
    onSuccess: () => notifyDone("Письмо со ссылкой отправлено"),
    onError: (error) => notifyError("отправить ссылку", error),
  });
}

/** Отключить 2FA (DELETE /auth/admin/users/{id}/two-factor) — после недавнего входа. */
export function useDisableTwoFactor(userId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.delete(`/auth/admin/users/${userId}/two-factor`),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: userQueries.detail(userId).queryKey });
      notifyDone("2FA отключена");
    },
    onError: (error) => notifyError("отключить 2FA", error),
  });
}
