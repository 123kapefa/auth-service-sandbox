import { useMutation, useQueryClient } from "@tanstack/react-query";
import { type Branding, brandingApi, brandingQueries } from "@/entities/branding";
import { notifyDone, notifyError } from "@/shared/lib/notify";

/** Сохранить оформление (PUT /auth/admin/branding): страницы входа покажут его при следующем открытии. */
export function useSaveBranding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (branding: Branding) => brandingApi.save(branding),
    onSuccess: async (saved) => {
      queryClient.setQueryData(brandingQueries.current().queryKey, saved);
      await queryClient.invalidateQueries({ queryKey: brandingQueries.all() });
      notifyDone("Оформление сохранено");
    },
    onError: (error) => notifyError("сохранение оформления", error),
  });
}
