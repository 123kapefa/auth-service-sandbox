import { useState } from "react";
import { type UserAppRoles } from "@/entities/app-user";
import { Badge, Button } from "@/shared/ui";
import { useSetUserRoles } from "../model/use-set-user-roles";

interface Props {
  clientId: string;
  data: UserAppRoles;
}

/**
 * Роли пользователя в приложении: отметить нужные и сохранить. Роль = режим: человек работает в одной из своих
 * ролей за раз. Роли по умолчанию (customer) есть у всех — их не снять. Изменение доходит до пользователя сразу:
 * его клиенты получат новые права без перелогина.
 */
export function UserRolesEditor({ clientId, data }: Props) {
  const [selected, setSelected] = useState(() => new Set(data.roles));
  const mutation = useSetUserRoles(clientId, data.userId);
  const changed =
    selected.size !== data.roles.length || data.roles.some((role) => !selected.has(role));

  const toggle = (role: string) =>
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(role)) {
        next.delete(role);
      } else {
        next.add(role);
      }
      return next;
    });

  return (
    <div className="grid gap-3">
      {data.defaultRoles.length > 0 && (
        <p className="text-sm text-muted-foreground">
          У всех:{" "}
          {data.defaultRoles.map((role) => (
            <Badge key={role}>{role}</Badge>
          ))}
        </p>
      )}
      <fieldset className="grid gap-2">
        <legend className="sr-only">Роли</legend>
        {data.assignableRoles.map((role) => (
          <label key={role.name} className="flex items-start gap-2 text-sm">
            <input
              type="checkbox"
              className="mt-1"
              checked={selected.has(role.name)}
              onChange={() => toggle(role.name)}
            />
            <span>
              <span className="font-medium">{role.name}</span>
              {role.description && (
                <span className="text-muted-foreground"> — {role.description}</span>
              )}
            </span>
          </label>
        ))}
      </fieldset>
      <p className="text-xs text-muted-foreground">
        Активный режим: {data.activeMode ?? "не выбран"}
      </p>
      <div>
        <Button
          size="sm"
          disabled={!changed || mutation.isPending}
          onClick={() => mutation.mutate([...selected].sort())}
        >
          Сохранить роли
        </Button>
      </div>
    </div>
  );
}
