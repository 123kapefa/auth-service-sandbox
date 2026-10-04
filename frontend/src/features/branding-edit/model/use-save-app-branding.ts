import { useMutation, useQueryClient } from "@tanstack/react-query";
import { type AppBrandingOwn, brandingApi, brandingQueries } from "@/entities/branding";
import { notifyDone, notifyError } from "@/shared/lib/notify";

/** Сохранить оформление приложения (PUT /auth/admin/apps/{clientId}/branding). */
export function useSaveAppBranding(clientId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (branding: AppBrandingOwn) => brandingApi.saveApp(clientId, branding),
    onSuccess: async (saved) => {
      queryClient.setQueryData(brandingQueries.app(clientId).queryKey, saved);
      await queryClient.invalidateQueries({ queryKey: brandingQueries.all() });
      notifyDone("Оформление приложения сохранено");
    },
    onError: (error) => notifyError("сохранение оформления приложения", error),
  });
}
