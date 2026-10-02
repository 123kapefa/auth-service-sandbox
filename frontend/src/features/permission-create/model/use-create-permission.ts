import { useMutation, useQueryClient } from "@tanstack/react-query";
import { appCatalogApi, appCatalogQueries } from "@/entities/app-catalog";
import { notifyDone, notifyError } from "@/shared/lib/notify";

export function useCreatePermission(clientId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (permission: { name: string; description: string }) =>
      appCatalogApi.createPermission(clientId, permission),
    onSuccess: async (_list, permission) => {
      await queryClient.invalidateQueries({ queryKey: appCatalogQueries.all(clientId) });
      notifyDone(`Право ${permission.name} создано`);
    },
    onError: (error) => notifyError("создать право", error),
  });
}
