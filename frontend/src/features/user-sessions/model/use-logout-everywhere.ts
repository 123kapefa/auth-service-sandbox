import { useMutation, useQueryClient } from "@tanstack/react-query";
import { userQueries } from "@/entities/user";
import { apiClient } from "@/shared/api";
import { notifyDone, notifyError } from "@/shared/lib/notify";

/** «Выйти везде» за пользователя (POST /auth/admin/users/{id}/logout). */
export function useLogoutEverywhere(userId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.post(`/auth/admin/users/${userId}/logout`),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: userQueries.sessions(userId).queryKey });
      notifyDone("Все сессии завершены");
    },
    onError: (error) => notifyError("выйти везде", error),
  });
}
