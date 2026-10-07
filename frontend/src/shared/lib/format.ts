const dateTime = new Intl.DateTimeFormat("ru-RU", { dateStyle: "short", timeStyle: "short" });
const dateOnly = new Intl.DateTimeFormat("ru-RU", { dateStyle: "short" });

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

/** Дата без времени по-русски или «—» (начало эпохи — «неизвестно», как в formatDateTime). */
export function formatDate(value: string | null | undefined): string {
  const date = value ? new Date(value) : null;
  return date && date.getTime() > 0 ? dateOnly.format(date) : "—";
}

/** Российский номер — «+7 912 345-67-89»; другой — как есть. */
export function formatPhone(value: string): string {
  const match = /^\+?7(\d{3})(\d{3})(\d{2})(\d{2})$/.exec(value);
  return match ? `+7 ${match[1]} ${match[2]}-${match[3]}-${match[4]}` : value;
}
