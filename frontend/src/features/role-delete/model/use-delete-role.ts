import { useMutation, useQueryClient } from "@tanstack/react-query";
import { appCatalogApi } from "@/entities/app-catalog";
import { notifyDone, notifyError } from "@/shared/lib/notify";

export function useDeleteRole(clientId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (name: string) => appCatalogApi.deleteRole(clientId, name),
    onSuccess: async (_roles, name) => {
      await queryClient.invalidateQueries({ queryKey: ["apps", clientId] });
      notifyDone(`Роль ${name} удалена`);
    },
    onError: (error) => notifyError("удалить роль", error),
  });
}
