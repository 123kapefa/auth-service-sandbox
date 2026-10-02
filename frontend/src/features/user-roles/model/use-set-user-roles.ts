import { useMutation, useQueryClient } from "@tanstack/react-query";
import { appUserApi, appUserQueries } from "@/entities/app-user";
import { userQueries } from "@/entities/user";
import { notifyDone, notifyError } from "@/shared/lib/notify";

/** Выданные роли в приложении — ровно этот список (PUT /auth/admin/apps/{clientId}/users/{id}/roles). */
export function useSetUserRoles(clientId: string, userId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (roles: string[]) => appUserApi.setRoles(clientId, userId, roles),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: appUserQueries.all(clientId) }),
        queryClient.invalidateQueries({ queryKey: userQueries.all() }),
      ]);
      notifyDone("Роли сохранены");
    },
    onError: (error) => notifyError("сохранить роли", error),
  });
}
