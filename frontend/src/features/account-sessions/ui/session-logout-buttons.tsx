import { useState } from "react";
import { Button, ConfirmDialog } from "@/shared/ui";
import {
  useLogoutEverywhere,
  useLogoutOthers,
  useTerminateSession,
} from "../model/use-session-logout";

/** «Выйти» на другом устройстве: без подтверждения — войти там можно снова. */
export function TerminateSessionButton({
  sessionId,
  device,
}: {
  sessionId: string;
  device: string;
}) {
  const mutation = useTerminateSession();
  return (
    <Button
      size="sm"
      variant="destructive"
      aria-label={`Выйти: ${device}`}
      disabled={mutation.isPending}
      onClick={() => mutation.mutate(sessionId)}
    >
      <Cross />
      Выйти
    </Button>
  );
}

/** «Выйти на других» — все сессии, кроме этой. */
export function LogoutOthersButton({ disabled }: { disabled?: boolean }) {
  const mutation = useLogoutOthers();
  return (
    <Button
      variant="destructive"
      disabled={disabled || mutation.isPending}
      onClick={() => mutation.mutate()}
    >
      <Cross />
      Выйти на других
    </Button>
  );
}

/** «Выйти везде» — с подтверждением: выйдет и здесь, понадобится войти снова. */
export function LogoutEverywhereButton() {
  const [open, setOpen] = useState(false);
  const mutation = useLogoutEverywhere();
  return (
    <>
      <Button variant="outline" onClick={() => setOpen(true)}>
        Выйти везде
      </Button>
      <ConfirmDialog
        open={open}
        onOpenChange={setOpen}
        title="Выйти на всех устройствах?"
        description="Сессии завершатся везде, включая это устройство и все приложения проекта. Войти можно снова."
        confirmLabel="Выйти везде"
        destructive
        pending={mutation.isPending}
        onConfirm={() => mutation.mutate()}
      />
    </>
  );
}

function Cross() {
  return (
    <span className="font-mono font-bold" aria-hidden>
      ×
    </span>
  );
}
