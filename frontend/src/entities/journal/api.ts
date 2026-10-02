import { keepPreviousData, queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope, unwrap } from "@/shared/api";
import { type AuditFilter, type AuditPage, type LoginEventsPage } from "./types";

const PAGE_SIZE = 25;

// Пустые фильтры не отправляются; «по» — включительно, до конца дня (время сервера — UTC).
const auditParams = (filter: AuditFilter) => ({
  action: filter.action || undefined,
  app: filter.app || undefined,
  objectId: filter.objectId || undefined,
  from: filter.from ? `${filter.from}T00:00:00Z` : undefined,
  to: filter.to ? `${filter.to}T23:59:59Z` : undefined,
  page: filter.page,
  pageSize: PAGE_SIZE,
});

export const journalApi = {
  audit: async (filter: AuditFilter) =>
    unwrap(
      await apiClient.get<Envelope<AuditPage>>("/auth/admin/audit", {
        params: auditParams(filter),
      }),
    ),
  appAudit: async (clientId: string, filter: AuditFilter) =>
    unwrap(
      await apiClient.get<Envelope<AuditPage>>(`/auth/admin/apps/${clientId}/audit`, {
        params: { ...auditParams(filter), app: undefined },
      }),
    ),
  loginEvents: async (userId: string, page: number) =>
    unwrap(
      await apiClient.get<Envelope<LoginEventsPage>>("/auth/admin/login-events", {
        params: { userId: userId || undefined, page, pageSize: PAGE_SIZE },
      }),
    ),
};

export const journalQueries = {
  /** clientId — журнал одного приложения (его админ), null — весь проект. */
  audit: (clientId: string | null, filter: AuditFilter) =>
    queryOptions({
      queryKey: ["journal", "audit", clientId, filter],
      queryFn: () => (clientId ? journalApi.appAudit(clientId, filter) : journalApi.audit(filter)),
      placeholderData: keepPreviousData,
    }),
  loginEvents: (userId: string, page: number) =>
    queryOptions({
      queryKey: ["journal", "logins", userId, page],
      queryFn: () => journalApi.loginEvents(userId, page),
      placeholderData: keepPreviousData,
    }),
};
