import { useMutation, useQueryClient } from "@tanstack/react-query";
import { userQueries } from "@/entities/user";
import { apiClient } from "@/shared/api";
import { notifyDone, notifyError } from "@/shared/lib/notify";

/** Открыть или закрыть доступ к одному приложению (PUT /auth/admin/users/{id}/apps/{clientId}/access). */
export function useSetAppAccess(userId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ clientId, hasAccess }: { clientId: string; hasAccess: boolean }) =>
      apiClient.put(`/auth/admin/users/${userId}/apps/${clientId}/access`, { hasAccess }),
    onSuccess: async (_data, { clientId, hasAccess }) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: userQueries.all() }),
        queryClient.invalidateQueries({ queryKey: ["apps", clientId] }),
      ]);
      notifyDone(hasAccess ? "Доступ открыт" : "Доступ закрыт");
    },
    onError: (error, { hasAccess }) =>
      notifyError(hasAccess ? "открыть доступ" : "закрыть доступ", error),
  });
}
