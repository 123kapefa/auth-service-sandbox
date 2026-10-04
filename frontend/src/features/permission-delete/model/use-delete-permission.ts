import { useMutation, useQueryClient } from "@tanstack/react-query";
import { appCatalogApi, appCatalogQueries } from "@/entities/app-catalog";
import { notifyDone, notifyError } from "@/shared/lib/notify";

export function useDeletePermission(clientId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (name: string) => appCatalogApi.deletePermission(clientId, name),
    onSuccess: async (_list, name) => {
      await queryClient.invalidateQueries({ queryKey: appCatalogQueries.all(clientId) });
      notifyDone(`Право ${name} удалено`);
    },
    onError: (error) => notifyError("удалить право", error),
  });
}
