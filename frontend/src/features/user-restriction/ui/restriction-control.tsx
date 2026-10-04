import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { type UserDetail } from "@/entities/user";
import { Button, ConfirmDialog, Dialog, FieldError, Label, Textarea } from "@/shared/ui";
import { type BlockForm, blockSchema } from "../model/schemas";
import { useSetRestriction } from "../model/use-set-restriction";

/** Блокировка аккаунта целиком: вход во все приложения закрыт, сессии завершены. */
export function RestrictionControl({ user }: { user: UserDetail }) {
  const [open, setOpen] = useState(false);
  const mutation = useSetRestriction(user.id);
  const form = useForm<BlockForm>({
    resolver: zodResolver(blockSchema),
    defaultValues: { reason: "" },
  });

  if (user.isRestricted) {
    return (
      <>
        <Button variant="outline" onClick={() => setOpen(true)}>
          Разблокировать
        </Button>
        <ConfirmDialog
          open={open}
          onOpenChange={setOpen}
          title="Разблокировать аккаунт?"
          description={`${user.username} снова сможет входить во все приложения.`}
          confirmLabel="Разблокировать"
          pending={mutation.isPending}
          onConfirm={() =>
            mutation.mutate({ restricted: false }, { onSuccess: () => setOpen(false) })
          }
        />
      </>
    );
  }

  const submit = form.handleSubmit(({ reason }) =>
    mutation.mutate(
      { restricted: true, reason: reason.trim() || undefined },
      {
        onSuccess: () => {
          setOpen(false);
          form.reset();
        },
      },
    ),
  );

  return (
    <>
      <Button variant="destructive" onClick={() => setOpen(true)}>
        Заблокировать
      </Button>
      <Dialog
        open={open}
        onOpenChange={setOpen}
        title="Заблокировать аккаунт?"
        description={`${user.username} не сможет войти ни в одно приложение, открытые сессии завершатся.`}
      >
        <form onSubmit={submit} className="grid gap-3" noValidate>
          <div className="grid gap-1">
            <Label htmlFor="reason">Причина (видна сотрудникам)</Label>
            <Textarea
              id="reason"
              aria-invalid={form.formState.errors.reason ? true : undefined}
              aria-describedby="reason-error"
              {...form.register("reason")}
            />
            <FieldError id="reason-error" message={form.formState.errors.reason?.message} />
          </div>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Отмена
            </Button>
            <Button type="submit" variant="destructive" disabled={mutation.isPending}>
              Заблокировать
            </Button>
          </div>
        </form>
      </Dialog>
    </>
  );
}
