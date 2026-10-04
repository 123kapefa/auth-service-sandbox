import { useState } from "react";
import { Button, ConfirmDialog } from "@/shared/ui";
import { useDeletePermission } from "../model/use-delete-permission";

export function DeletePermissionButton({ clientId, name }: { clientId: string; name: string }) {
  const [open, setOpen] = useState(false);
  const mutation = useDeletePermission(clientId);

  return (
    <>
      <Button size="sm" variant="ghost" onClick={() => setOpen(true)}>
        Удалить
      </Button>
      <ConfirmDialog
        open={open}
        onOpenChange={setOpen}
        title={`Удалить право ${name}?`}
        description="Право пропадёт из всех ролей; кто работал в них — обновят токен и останутся без него."
        confirmLabel="Удалить"
        destructive
        pending={mutation.isPending}
        onConfirm={() => mutation.mutate(name, { onSuccess: () => setOpen(false) })}
      />
    </>
  );
}
