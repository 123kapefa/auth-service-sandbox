/** Профиль в кабинете (GET /me/profile): данные учётной записи на сервере входа. */
export interface AccountProfile {
  id: string;
  /** Телефон с «+» — логин; меняется только через поддержку. */
  phone: string | null;
  email: string | null;
  emailVerified: boolean;
  createdAt: string;
}
