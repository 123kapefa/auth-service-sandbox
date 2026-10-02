const dateTime = new Intl.DateTimeFormat("ru-RU", { dateStyle: "short", timeStyle: "short" });

/**
 * Дата и время по-русски или «—». Начало эпохи — «неизвестно»: у пользователей из импорта realm Keycloak не хранит
 * дату создания и отдаёт 0.
 */
export function formatDateTime(value: string | null | undefined): string {
  const date = value ? new Date(value) : null;
  return date && date.getTime() > 0 ? dateTime.format(date) : "—";
}
