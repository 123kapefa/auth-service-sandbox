import { useState } from "react";
import { Button, ConfirmDialog } from "@/shared/ui";
import { useDeleteRole } from "../model/use-delete-role";

export function DeleteRoleButton({ clientId, name }: { clientId: string; name: string }) {
  const [open, setOpen] = useState(false);
  const mutation = useDeleteRole(clientId);

  return (
    <>
      <Button size="sm" variant="ghost" onClick={() => setOpen(true)}>
        Удалить
      </Button>
      <ConfirmDialog
        open={open}
        onOpenChange={setOpen}
        title={`Удалить роль ${name}?`}
        description="У всех, кому она выдана, роль пропадёт; кто работал в этом режиме — обновят токен и останутся без её прав."
        confirmLabel="Удалить"
        destructive
        pending={mutation.isPending}
        onConfirm={() => mutation.mutate(name, { onSuccess: () => setOpen(false) })}
      />
    </>
  );
}
