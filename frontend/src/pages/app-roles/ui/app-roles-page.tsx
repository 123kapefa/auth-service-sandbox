import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useParams } from "react-router";
import { appCatalogQueries, type AppRole } from "@/entities/app-catalog";
import { CreatePermissionButton } from "@/features/permission-create";
import { DeletePermissionButton } from "@/features/permission-delete";
import { DeleteRoleButton } from "@/features/role-delete";
import { RoleDialog } from "@/features/role-editor";
import { getErrorMessage } from "@/shared/api";
import { Badge, Button, Card, CardTitle, ErrorState, Loading } from "@/shared/ui";

/**
 * Роли (они же режимы) и права приложения. Встроенные роли не удалить; у admin не убрать roles.manage. Системные права
 * объявлены кодом приложения — их не удалить; свои — создаются здесь.
 */
export function AppRolesPage() {
  const { clientId = "" } = useParams();
  const roles = useQuery(appCatalogQueries.roles(clientId));
  const permissions = useQuery(appCatalogQueries.permissions(clientId));
  const [editing, setEditing] = useState<AppRole | "new" | null>(null);

  if (roles.isPending || permissions.isPending) {
    return <Loading />;
  }

  if (roles.isError || permissions.isError) {
    return (
      <ErrorState
        message={getErrorMessage(
          roles.error ?? permissions.error,
          "Не удалось загрузить роли и права",
        )}
      />
    );
  }

  const describe = new Map(
    permissions.data.map((permission) => [permission.name, permission.description]),
  );
  return (
    <div className="grid gap-4 lg:grid-cols-[2fr_1fr]">
      <Card>
        <div className="mb-3 flex items-center justify-between">
          <CardTitle className="mb-0">Роли</CardTitle>
          <Button size="sm" onClick={() => setEditing("new")}>
            Новая роль
          </Button>
        </div>
        <ul className="grid gap-3">
          {roles.data.map((role) => (
            <li key={role.name} className="rounded-md border border-border p-3">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-medium">{role.name}</span>
                {role.builtin && <Badge>встроенная</Badge>}
                <span className="text-sm text-muted-foreground">{role.description}</span>
                <span className="ml-auto flex gap-1">
                  <Button size="sm" variant="ghost" onClick={() => setEditing(role)}>
                    Изменить
                  </Button>
                  {!role.builtin && <DeleteRoleButton clientId={clientId} name={role.name} />}
                </span>
              </div>
              <div className="mt-2 flex flex-wrap gap-1">
                {role.permissions.length === 0 ? (
                  <span className="text-xs text-muted-foreground">без прав</span>
                ) : (
                  role.permissions.map((permission) => (
                    <Badge key={permission} title={describe.get(permission) ?? undefined}>
                      {permission}
                    </Badge>
                  ))
                )}
              </div>
            </li>
          ))}
        </ul>
      </Card>
      <Card>
        <div className="mb-3 flex items-center justify-between">
          <CardTitle className="mb-0">Права</CardTitle>
          <CreatePermissionButton clientId={clientId} />
        </div>
        <ul className="grid gap-2 text-sm">
          {permissions.data.map((permission) => (
            <li key={permission.name} className="flex items-start justify-between gap-2">
              <span>
                <span className="font-medium">{permission.name}</span>{" "}
                <Badge>{permission.source === "admin" ? "своё" : "из кода"}</Badge>
                {permission.description && (
                  <span className="block text-muted-foreground">{permission.description}</span>
                )}
              </span>
              {permission.source === "admin" && (
                <DeletePermissionButton clientId={clientId} name={permission.name} />
              )}
            </li>
          ))}
        </ul>
      </Card>
      <RoleDialog
        clientId={clientId}
        role={editing === "new" ? null : editing}
        permissions={permissions.data}
        open={editing !== null}
        onOpenChange={(open) => !open && setEditing(null)}
      />
    </div>
  );
}
