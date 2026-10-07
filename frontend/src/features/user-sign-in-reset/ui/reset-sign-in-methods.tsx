import { useState } from "react";
import { Button, ConfirmDialog } from "@/shared/ui";
import { useResetSignInMethods } from "../model/use-reset-sign-in-methods";

/**
 * Панель «Вернуть способы по умолчанию» (экран 07 этапа 10): человек потерял ключи и коды — админ включает пароль и коды
 * обратно (ключи доступа остаются). Себе — нельзя (blocked — почему кнопка неактивна).
 */
export function ResetSignInMethods({
  userId,
  blocked,
}: {
  userId: string;
  blocked: string | null;
}) {
  const [confirm, setConfirm] = useState(false);
  const reset = useResetSignInMethods(userId, () => setConfirm(false));

  return (
    <div className="mt-4 border border-dashed border-primary bg-hatch p-4 text-sm">
      <p>
        Человек потерял ключи и коды восстановления? Верните способы по умолчанию: включатся пароль,
        код на телефон и почта, ключи доступа останутся. Человек получит уведомление, действие
        попадёт в журнал.
      </p>
      <div className="mt-3 flex flex-wrap items-center gap-3">
        <Button
          size="sm"
          variant="destructive"
          disabled={blocked !== null}
          onClick={() => setConfirm(true)}
        >
          Вернуть способы по умолчанию
        </Button>
        {blocked && <span className="text-muted-foreground">{blocked}</span>}
      </div>
      <ConfirmDialog
        open={confirm}
        onOpenChange={setConfirm}
        title="Вернуть способы по умолчанию?"
        description="Пароль, код на телефон и код на почту снова заработают. Человеку придёт письмо (или SMS), действие попадёт в журнал."
        confirmLabel="Вернуть"
        destructive
        pending={reset.isPending}
        onConfirm={() => reset.mutate()}
      />
    </div>
  );
}
