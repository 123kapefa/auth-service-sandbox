/** Режим пользователя в приложении проекта (GET /auth/modes). Режим — его роль в этом приложении. */
export interface AppMode {
  /** Код приложения (client_id): orders; сервис входа — тоже приложение. */
  app: string;
  name: string | null;
  modes: string[];
  /** Выбранный; не выбирали — роль по умолчанию (так считает и токен приложения). */
  activeMode: string | null;
}
