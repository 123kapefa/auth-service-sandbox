import { useMutation, useQueryClient } from "@tanstack/react-query";
import { serviceKeyApi, serviceKeyQueries } from "@/entities/service-key";
import { notifyDone, notifyError } from "@/shared/lib/notify";

export function useRotateServiceKey() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ code, keepTokens }: { code: string; keepTokens: boolean }) =>
      serviceKeyApi.rotate(code, keepTokens),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: serviceKeyQueries.all() });
    },
    onError: (error) => notifyError("перевыпустить ключ", error),
  });
}

export function useRevokeServiceKey() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (code: string) => serviceKeyApi.revoke(code),
    onSuccess: async (_result, code) => {
      await queryClient.invalidateQueries({ queryKey: serviceKeyQueries.all() });
      notifyDone(`Ключ сервиса ${code} отозван`);
    },
    onError: (error) => notifyError("отозвать ключ", error),
  });
}

export function useSetServicePermissions() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ code, permissions }: { code: string; permissions: string[] }) =>
      serviceKeyApi.setPermissions(code, permissions),
    onSuccess: async (_service, { code }) => {
      await queryClient.invalidateQueries({ queryKey: serviceKeyQueries.all() });
      notifyDone(`Права сервиса ${code} сохранены`);
    },
    onError: (error) => notifyError("сохранить права сервиса", error),
  });
}
