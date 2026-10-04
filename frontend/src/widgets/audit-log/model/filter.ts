import { type AuditFilter } from "@/entities/journal";

const KEYS = ["action", "app", "from", "to", "objectId"] as const;

/** Фильтр журнала — из адресной строки (ссылкой можно поделиться, «назад» работает). */
export function filterFromParams(params: URLSearchParams): AuditFilter {
  return {
    action: params.get("action") ?? "",
    app: params.get("app") ?? "",
    from: params.get("from") ?? "",
    to: params.get("to") ?? "",
    objectId: params.get("objectId") ?? "",
    page: Number(params.get("page") ?? "1") || 1,
  };
}

/** Обратно в адрес: только заданные поля; сменился фильтр — с первой страницы. */
export function paramsFromFilter(filter: AuditFilter, keep: URLSearchParams): URLSearchParams {
  const next = new URLSearchParams(keep);
  for (const key of KEYS) {
    if (filter[key]) {
      next.set(key, filter[key]);
    } else {
      next.delete(key);
    }
  }

  if (filter.page > 1) {
    next.set("page", String(filter.page));
  } else {
    next.delete("page");
  }

  return next;
}
