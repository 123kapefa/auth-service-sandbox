import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { type IssuedServiceKey, SecretDialog, SERVICE_PERMISSIONS } from "@/entities/service-key";
import { Button, Dialog, FieldError, Input, Label } from "@/shared/ui";
import { type ServiceKeyForm, serviceKeySchema } from "../model/schemas";
import { useIssueServiceKey } from "../model/use-issue-service-key";

/** Новый сервис проекта: код, название, права в сервисе входа → ключ (секрет — один раз, в отдельном окне). */
export function IssueServiceKeyButton() {
  const [open, setOpen] = useState(false);
  const [issued, setIssued] = useState<IssuedServiceKey | null>(null);
  const mutation = useIssueServiceKey();
  const form = useForm<ServiceKeyForm>({
    resolver: zodResolver(serviceKeySchema),
    defaultValues: { code: "", name: "", permissions: [] },
  });
  const errors = form.formState.errors;

  const submit = form.handleSubmit((values) =>
    mutation.mutate(
      { code: values.code, name: values.name.trim(), permissions: values.permissions },
      {
        onSuccess: (result) => {
          setOpen(false);
          form.reset();
          setIssued(result);
          mutation.reset(); // секрет — только в окне, не в состоянии мутации
        },
      },
    ),
  );

  return (
    <>
      <Button size="sm" onClick={() => setOpen(true)}>
        Добавить сервис
      </Button>
      <Dialog
        open={open}
        onOpenChange={(next) => {
          setOpen(next);
          if (!next) {
            form.reset();
          }
        }}
        title="Новый сервис"
        description="Сервис получит ключ: по нему он берёт токены и ходит к другим сервисам проекта."
      >
        <form onSubmit={submit} className="grid gap-3" noValidate>
          <div className="grid gap-1">
            <Label htmlFor="service-code">Код</Label>
            <Input
              id="service-code"
              placeholder="orders"
              aria-invalid={errors.code ? true : undefined}
              aria-describedby="service-code-error"
              {...form.register("code")}
            />
            <FieldError id="service-code-error" message={errors.code?.message} />
          </div>
          <div className="grid gap-1">
            <Label htmlFor="service-name">Название</Label>
            <Input
              id="service-name"
              placeholder="Заказы"
              aria-invalid={errors.name ? true : undefined}
              aria-describedby="service-name-error"
              {...form.register("name")}
            />
            <FieldError id="service-name-error" message={errors.name?.message} />
          </div>
          <fieldset className="grid gap-2">
            <legend className="mb-1 text-sm font-medium">Что можно в сервисе входа</legend>
            {SERVICE_PERMISSIONS.map((permission) => (
              <label key={permission.name} className="flex items-start gap-2 text-sm">
                <input
                  type="checkbox"
                  value={permission.name}
                  className="mt-1"
                  {...form.register("permissions")}
                />
                <span>
                  {permission.label}
                  <span className="block text-xs text-muted-foreground">{permission.hint}</span>
                </span>
              </label>
            ))}
          </fieldset>
          <div className="flex justify-end gap-2">
            <Button
              type="button"
              variant="outline"
              onClick={() => {
                setOpen(false);
                form.reset();
              }}
            >
              Отмена
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              Выдать ключ
            </Button>
          </div>
        </form>
      </Dialog>
      <SecretDialog issued={issued} onClose={() => setIssued(null)} />
    </>
  );
}
