import { useState } from "react";
import { Button, ConfirmDialog } from "@/shared/ui";
import { useSetAppAccess } from "../model/use-set-app-access";

interface Props {
  userId: string;
  clientId: string;
  appName: string;
  hasAccess: boolean;
}

/** Доступ к одному приложению: аккаунт рабочий, в остальные приложения человек входит как обычно. */
export function AppAccessToggle({ userId, clientId, appName, hasAccess }: Props) {
  const [open, setOpen] = useState(false);
  const mutation = useSetAppAccess(userId);

  if (!hasAccess) {
    return (
      <Button
        size="sm"
        variant="outline"
        disabled={mutation.isPending}
        onClick={() => mutation.mutate({ clientId, hasAccess: true })}
      >
        Открыть доступ
      </Button>
    );
  }

  return (
    <>
      <Button size="sm" variant="outline" onClick={() => setOpen(true)}>
        Закрыть доступ
      </Button>
      <ConfirmDialog
        open={open}
        onOpenChange={setOpen}
        title={`Закрыть доступ к «${appName}»?`}
        description="В это приложение человек не войдёт, его открытые сессии в нём перестанут работать. Остальные приложения — как прежде."
        confirmLabel="Закрыть доступ"
        destructive
        pending={mutation.isPending}
        onConfirm={() =>
          mutation.mutate({ clientId, hasAccess: false }, { onSuccess: () => setOpen(false) })
        }
      />
    </>
  );
}
