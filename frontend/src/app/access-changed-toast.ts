import { type QueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { type AccessChanges, setAccessChangedListener } from "@/shared/api";

/** Умный рефреш: плашка «стало доступно / больше недоступно» и перечитать данные под новыми правами. */
export function listenAccessChanges(queryClient: QueryClient): void {
  setAccessChangedListener((changes: AccessChanges) => {
    const lines = [
      changes.addedPermissions.length > 0 &&
        `Стало доступно: ${changes.addedPermissions.join(", ")}`,
      changes.removedPermissions.length > 0 &&
        `Больше недоступно: ${changes.removedPermissions.join(", ")}`,
      changes.addedRoles.length > 0 && `Новые роли: ${changes.addedRoles.join(", ")}`,
      changes.removedRoles.length > 0 && `Сняты роли: ${changes.removedRoles.join(", ")}`,
    ].filter(Boolean);
    toast.info("Ваш доступ изменился", {
      description: lines.join(". ") || undefined,
      duration: 10_000,
    });
    void queryClient.invalidateQueries();
  });
}
