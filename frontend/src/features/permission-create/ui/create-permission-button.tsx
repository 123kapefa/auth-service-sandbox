import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Button, Dialog, FieldError, Input, Label, Textarea } from "@/shared/ui";
import { type PermissionForm, permissionSchema } from "../model/schemas";
import { useCreatePermission } from "../model/use-create-permission";

/** Своё право приложения: проверять его будет код приложения — без этого право ничего не даёт. */
export function CreatePermissionButton({ clientId }: { clientId: string }) {
  const [open, setOpen] = useState(false);
  const mutation = useCreatePermission(clientId);
  const form = useForm<PermissionForm>({
    resolver: zodResolver(permissionSchema),
    defaultValues: { name: "", description: "" },
  });
  const errors = form.formState.errors;

  const submit = form.handleSubmit((values) =>
    mutation.mutate(
      { name: values.name, description: values.description.trim() },
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
      <Button size="sm" variant="outline" onClick={() => setOpen(true)}>
        Новое право
      </Button>
      <Dialog
        open={open}
        onOpenChange={setOpen}
        title="Новое право"
        description="Право начнёт что-то давать, когда его станет проверять код приложения."
      >
        <form onSubmit={submit} className="grid gap-3" noValidate>
          <div className="grid gap-1">
            <Label htmlFor="permission-name">Имя</Label>
            <Input
              id="permission-name"
              placeholder="reports.export"
              aria-invalid={errors.name ? true : undefined}
              aria-describedby="permission-name-error"
              {...form.register("name")}
            />
            <FieldError id="permission-name-error" message={errors.name?.message} />
          </div>
          <div className="grid gap-1">
            <Label htmlFor="permission-description">Описание</Label>
            <Textarea
              id="permission-description"
              aria-invalid={errors.description ? true : undefined}
              aria-describedby="permission-description-error"
              {...form.register("description")}
            />
            <FieldError id="permission-description-error" message={errors.description?.message} />
          </div>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Отмена
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              Создать
            </Button>
          </div>
        </form>
      </Dialog>
    </>
  );
}
