import { useMutation, useQueryClient } from "@tanstack/react-query";
import { appCatalogApi, appCatalogQueries, type RoleDraft } from "@/entities/app-catalog";
import { notifyDone, notifyError } from "@/shared/lib/notify";

/** Создать роль или изменить описание и состав (ровно этот список прав). */
export function useSaveRole(clientId: string, mode: "create" | "update") {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (role: RoleDraft) =>
      mode === "create"
        ? appCatalogApi.createRole(clientId, role)
        : appCatalogApi.updateRole(clientId, role),
    onSuccess: async (_roles, role) => {
      await queryClient.invalidateQueries({ queryKey: appCatalogQueries.all(clientId) });
      notifyDone(mode === "create" ? `Роль ${role.name} создана` : `Роль ${role.name} сохранена`);
    },
    onError: (error) => notifyError(mode === "create" ? "создать роль" : "сохранить роль", error),
  });
}
