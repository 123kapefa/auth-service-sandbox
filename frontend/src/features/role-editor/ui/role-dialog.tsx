import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { type AppPermission, type AppRole } from "@/entities/app-catalog";
import { Button, Dialog, FieldError, Input, Label, Textarea } from "@/shared/ui";
import { type RoleForm, roleSchema } from "../model/schemas";
import { useSaveRole } from "../model/use-save-role";

interface Props {
  clientId: string;
  /** null — новая роль. */
  role: AppRole | null;
  permissions: AppPermission[];
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Роль и её права. Изменение состава действует сразу: у всех, кто работает в этом режиме, клиенты обновят токен
 * без перелогина и получат новые права.
 */
export function RoleDialog({ clientId, role, permissions, open, onOpenChange }: Props) {
  const mode = role ? "update" : "create";
  const mutation = useSaveRole(clientId, mode);
  const form = useForm<RoleForm>({
    resolver: zodResolver(roleSchema),
    values: {
      name: role?.name ?? "",
      description: role?.description ?? "",
      permissions: role?.permissions ?? [],
    },
  });
  const errors = form.formState.errors;

  const submit = form.handleSubmit((values) =>
    mutation.mutate(
      {
        name: values.name,
        description: values.description.trim(),
        permissions: values.permissions,
      },
      { onSuccess: () => onOpenChange(false) },
    ),
  );

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={role ? `Роль ${role.name}` : "Новая роль"}
      description={
        role ? "Новые права получат все, кто работает в этой роли, — без перелогина." : undefined
      }
    >
      <form onSubmit={submit} className="grid gap-3" noValidate>
        {!role && (
          <div className="grid gap-1">
            <Label htmlFor="role-name">Имя</Label>
            <Input
              id="role-name"
              placeholder="support"
              aria-invalid={errors.name ? true : undefined}
              aria-describedby="role-name-error"
              {...form.register("name")}
            />
            <FieldError id="role-name-error" message={errors.name?.message} />
          </div>
        )}
        <div className="grid gap-1">
          <Label htmlFor="role-description">Описание</Label>
          <Textarea
            id="role-description"
            aria-invalid={errors.description ? true : undefined}
            aria-describedby="role-description-error"
            {...form.register("description")}
          />
          <FieldError id="role-description-error" message={errors.description?.message} />
        </div>
        <fieldset className="grid max-h-60 gap-1 overflow-y-auto">
          <legend className="mb-1 text-sm font-medium">Права</legend>
          {permissions.map((permission) => (
            <label key={permission.name} className="flex items-start gap-2 text-sm">
              <input
                type="checkbox"
                className="mt-1"
                value={permission.name}
                {...form.register("permissions")}
              />
              <span>
                <span className="font-medium">{permission.name}</span>
                {permission.description && (
                  <span className="text-muted-foreground"> — {permission.description}</span>
                )}
              </span>
            </label>
          ))}
        </fieldset>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            Отмена
          </Button>
          <Button type="submit" disabled={mutation.isPending}>
            {role ? "Сохранить" : "Создать"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
