import { useState } from "react";
import { type UserDetail } from "@/entities/user";
import { Button, ConfirmDialog } from "@/shared/ui";
import { useDisableTwoFactor, useSendPasswordReset } from "../model/use-credentials";

/**
 * Пароль и 2FA: пароль админ не видит и не задаёт — пользователь получает ссылку на почту. Вход по паролю человек
 * выключил сам (passwordDisabled) — ссылку не шлём: сервис ответит 409, сначала — вернуть способы по умолчанию.
 */
export function CredentialsActions({
  user,
  passwordDisabled = false,
}: {
  user: UserDetail;
  passwordDisabled?: boolean;
}) {
  const [confirm, setConfirm] = useState<"reset" | "twoFactor" | null>(null);
  const reset = useSendPasswordReset(user.id);
  const twoFactor = useDisableTwoFactor(user.id);

  return (
    <div className="flex flex-wrap gap-2">
      <Button
        size="sm"
        variant="outline"
        disabled={!user.email || passwordDisabled}
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
      {passwordDisabled ? (
        <p className="w-full text-sm text-muted-foreground">
          Ссылка не отправляется: вход по паролю у человека выключен.
        </p>
      ) : (
        !user.email && (
          <p className="w-full text-xs text-muted-foreground">
            Почты нет — ссылку отправить некуда.
          </p>
        )
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
