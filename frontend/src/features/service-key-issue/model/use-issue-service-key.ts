import { useMutation, useQueryClient } from "@tanstack/react-query";
import { serviceKeyApi, serviceKeyQueries, type ServiceKeyDraft } from "@/entities/service-key";
import { notifyError } from "@/shared/lib/notify";

export function useIssueServiceKey() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (draft: ServiceKeyDraft) => serviceKeyApi.issue(draft),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: serviceKeyQueries.all() });
    },
    onError: (error) => notifyError("выдать ключ", error),
  });
}
