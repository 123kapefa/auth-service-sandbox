import { useState } from "react";
import { Button, ConfirmDialog } from "@/shared/ui";
import { useLogoutEverywhere } from "../model/use-logout-everywhere";

export function LogoutEverywhereButton({
  userId,
  disabled,
}: {
  userId: string;
  disabled?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const mutation = useLogoutEverywhere(userId);

  return (
    <>
      <Button size="sm" variant="outline" disabled={disabled} onClick={() => setOpen(true)}>
        Выйти везде
      </Button>
      <ConfirmDialog
        open={open}
        onOpenChange={setOpen}
        title="Завершить все сессии?"
        description="Пользователь выйдет на всех устройствах и во всех приложениях; войти снова сможет."
        confirmLabel="Выйти везде"
        destructive
        pending={mutation.isPending}
        onConfirm={() => mutation.mutate(undefined, { onSuccess: () => setOpen(false) })}
      />
    </>
  );
}
