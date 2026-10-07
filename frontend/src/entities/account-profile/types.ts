/** Профиль в кабинете (GET /me/profile): данные учётной записи на сервере входа. */
export interface AccountProfile {
  id: string;
  /** Телефон с «+» — логин; человек меняет его сам (действие update-phone). */
  phone: string | null;
  email: string | null;
  emailVerified: boolean;
  createdAt: string;
  /** Когда человек сам сменил номер; null — не менял. */
  phoneChangedAt?: string | null;
}
