/** Сервис проекта с ключом доступа (GET /auth/admin/services). Секрета в списке нет. */
export interface ServiceKey {
  code: string;
  clientId: string;
  name: string;
  issuedAt: string;
  rotatedAt: string | null;
  /** Что сервису можно в сервисе входа: service.users.read, service.access.check; null — не удалось прочитать. */
  permissions: string[] | null;
}

/** Ответ на выдачу и перевыпуск: секрет показывается один раз. */
export interface IssuedServiceKey {
  service: ServiceKey;
  clientSecret: string;
}

export interface ServiceKeyDraft {
  code: string;
  name: string;
  permissions: string[];
}

/** Права сервисов в сервисе входа (AuthServicePermissions.ServiceAccess на сервере). */
export const SERVICE_PERMISSIONS = [
  {
    name: "service.users.read",
    label: "Читать пользователя",
    hint: "Телефон, почта, заблокирован ли — по Id или логину",
  },
  {
    name: "service.access.check",
    label: "Проверять доступ пользователя",
    hint: "Есть ли у пользователя право в приложении — для фоновых задач без его токена",
  },
] as const;
