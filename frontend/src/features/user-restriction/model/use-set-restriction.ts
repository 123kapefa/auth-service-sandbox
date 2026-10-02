import { useMutation, useQueryClient } from "@tanstack/react-query";
import { userQueries } from "@/entities/user";
import { apiClient } from "@/shared/api";
import { notifyDone, notifyError } from "@/shared/lib/notify";

/** Заблокировать или разблокировать аккаунт (PUT /auth/admin/users/{id}/restriction). */
export function useSetRestriction(userId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (change: { restricted: boolean; reason?: string }) =>
      apiClient.put(`/auth/admin/users/${userId}/restriction`, change),
    onSuccess: async (_data, change) => {
      await queryClient.invalidateQueries({ queryKey: userQueries.all() });
      notifyDone(change.restricted ? "Аккаунт заблокирован" : "Аккаунт разблокирован");
    },
    onError: (error, change) =>
      notifyError(change.restricted ? "блокировка" : "разблокировка", error),
  });
}
