import { useQuery } from "@tanstack/react-query";
import { SERVICE_PERMISSIONS, serviceKeyQueries } from "@/entities/service-key";
import { IssueServiceKeyButton } from "@/features/service-key-issue";
import { ServiceKeyActions } from "@/features/service-key-manage";
import { getErrorMessage } from "@/shared/api";
import { formatDateTime } from "@/shared/lib/format";
import { Badge, Card, CardTitle, EmptyState, ErrorState, Loading } from "@/shared/ui";

const LABELS = new Map<string, string>(
  SERVICE_PERMISSIONS.map((permission) => [permission.name, permission.label]),
);

/**
 * Сервисы проекта и их ключи доступа (админ проекта). Ключ — client_id и секрет: по нему сервис берёт токены и ходит к
 * другим сервисам проекта; права — что ему можно в сервисе входа. Секрет виден только при выдаче и перевыпуске.
 */
export function ServicesPage() {
  const services = useQuery(serviceKeyQueries.list());

  if (services.isPending) {
    return <Loading />;
  }

  if (services.isError) {
    return <ErrorState message={getErrorMessage(services.error, "Не удалось загрузить сервисы")} />;
  }

  return (
    <Card>
      <div className="mb-3 flex items-center justify-between gap-2">
        <div>
          <CardTitle className="mb-0">Сервисы и ключи</CardTitle>
          <p className="text-sm text-muted-foreground">
            Сервис без ключа не получит токен и не попадёт ни в один сервис проекта.
          </p>
        </div>
        <IssueServiceKeyButton />
      </div>
      {services.data.length === 0 ? (
        <EmptyState>Сервисов с ключами пока нет</EmptyState>
      ) : (
        <ul className="grid gap-3">
          {services.data.map((service) => (
            <li key={service.code} className="rounded-md border border-border p-3">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-medium">{service.name}</span>
                <span className="font-mono text-sm text-muted-foreground">{service.clientId}</span>
                <span className="ml-auto">
                  <ServiceKeyActions service={service} />
                </span>
              </div>
              <div className="mt-2 flex flex-wrap gap-1">
                {service.permissions.length === 0 ? (
                  <span className="text-xs text-muted-foreground">в сервисе входа — без прав</span>
                ) : (
                  service.permissions.map((permission) => (
                    <Badge key={permission} title={permission}>
                      {LABELS.get(permission) ?? permission}
                    </Badge>
                  ))
                )}
              </div>
              <p className="mt-2 text-xs text-muted-foreground">
                Выдан {formatDateTime(service.issuedAt)}
                {service.rotatedAt ? ` · перевыпущен ${formatDateTime(service.rotatedAt)}` : ""}
              </p>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
