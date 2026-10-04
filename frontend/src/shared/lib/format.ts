const dateTime = new Intl.DateTimeFormat("ru-RU", { dateStyle: "short", timeStyle: "short" });

/**
 * Дата и время по-русски или «—». Начало эпохи — «неизвестно»: у пользователей из импорта realm Keycloak не хранит
 * дату создания и отдаёт 0.
 */
export function formatDateTime(value: string | null | undefined): string {
  const date = value ? new Date(value) : null;
  return date && date.getTime() > 0 ? dateTime.format(date) : "—";
}

/**
 * Давность по-русски: «только что», «5 мин назад», «3 ч назад»; старше суток — дата и время. now — для тестов.
 */
export function formatAgo(value: string, now = Date.now()): string {
  const minutes = Math.floor((now - new Date(value).getTime()) / 60_000);
  if (minutes < 1) {
    return "только что";
  }

  if (minutes < 60) {
    return `${minutes} мин назад`;
  }

  return minutes < 24 * 60 ? `${Math.floor(minutes / 60)} ч назад` : formatDateTime(value);
}
