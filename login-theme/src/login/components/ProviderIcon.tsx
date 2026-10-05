/**
 * Значок внешнего входа по alias провайдера realm: Госуслуги (esia), Яндекс ID (yandex). Простые знаки в цветах
 * сервисов — узнаваемые, без файлов логотипов. Неизвестный провайдер — первая буква названия.
 */
export function ProviderIcon({ alias, name }: { alias: string; name: string }) {
  switch (alias) {
    case "esia":
      return (
        <svg viewBox="0 0 24 24" className="size-6" aria-hidden="true">
          <rect width="24" height="24" fill="#0D4CD3" />
          <rect y="17" width="24" height="7" fill="#EE3F58" />
          <path d="M7 5h10v3H10.5v9H7z" fill="#fff" />
        </svg>
      );
    case "yandex":
      return (
        <svg viewBox="0 0 24 24" className="size-6" aria-hidden="true">
          <circle cx="12" cy="12" r="12" fill="#FC3F1D" />
          <path
            d="M13.3 19h2.2V5h-3.2c-3.2 0-4.9 1.6-4.9 4.1 0 2 .9 3.2 2.6 4.4L7 19h2.4l3-4.8-1-.7c-1.3-.9-1.9-1.6-1.9-3.1 0-1.3.9-2.2 2.7-2.2h1V19z"
            fill="#fff"
          />
        </svg>
      );
    default:
      return (
        <span aria-hidden="true" className="font-mono text-sm font-semibold">
          {name.slice(0, 1).toUpperCase()}
        </span>
      );
  }
}
