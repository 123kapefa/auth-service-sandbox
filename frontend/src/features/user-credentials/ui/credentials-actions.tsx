import { useState } from "react";
import { type UserDetail } from "@/entities/user";
import { Button, ConfirmDialog } from "@/shared/ui";
import { useDisableTwoFactor, useSendPasswordReset } from "../model/use-credentials";

/** Пароль и 2FA: пароль админ не видит и не задаёт — пользователь получает ссылку на почту. */
export function CredentialsActions({ user }: { user: UserDetail }) {
  const [confirm, setConfirm] = useState<"reset" | "twoFactor" | null>(null);
  const reset = useSendPasswordReset(user.id);
  const twoFactor = useDisableTwoFactor(user.id);

  return (
    <div className="flex flex-wrap gap-2">
      <Button
        size="sm"
        variant="outline"
        disabled={!user.email}
        onClick={() => setConfirm("reset")}
      >
        Отправить ссылку для смены пароля
      </Button>
      <Button
        size="sm"
        variant="outline"
        disabled={!user.hasTwoFactor}
        onClick={() => setConfirm("twoFactor")}
      >
        Отключить 2FA
      </Button>
      {!user.email && (
        <p className="w-full text-xs text-muted-foreground">Почты нет — ссылку отправить некуда.</p>
      )}
      <ConfirmDialog
        open={confirm === "reset"}
        onOpenChange={(open) => setConfirm(open ? "reset" : null)}
        title="Отправить ссылку для смены пароля?"
        description={`Письмо уйдёт на ${user.email ?? "почту пользователя"}; ссылка действует 24 часа.`}
        confirmLabel="Отправить"
        pending={reset.isPending}
        onConfirm={() => reset.mutate(undefined, { onSuccess: () => setConfirm(null) })}
      />
      <ConfirmDialog
        open={confirm === "twoFactor"}
        onOpenChange={(open) => setConfirm(open ? "twoFactor" : null)}
        title="Отключить 2FA?"
        description="Приложение-аутентификатор и коды восстановления будут удалены — аккаунт останется только под паролем. Включить 2FA заново пользователь сможет сам."
        confirmLabel="Отключить"
        destructive
        pending={twoFactor.isPending}
        onConfirm={() => twoFactor.mutate(undefined, { onSuccess: () => setConfirm(null) })}
      />
    </div>
  );
}
