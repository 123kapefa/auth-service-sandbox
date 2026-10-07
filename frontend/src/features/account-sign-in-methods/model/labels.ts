import { type SignInMethodKey } from "@/entities/account-security";

export const methodTitle: Record<SignInMethodKey, string> = {
  password: "Пароль",
  "phone-code": "Код на телефон",
  "email-code": "Код на почту",
};

/** Способ входа человека → способ проекта (10.7): выключенный в проекте кабинет не показывает. */
export const projectMethod: Record<SignInMethodKey, string> = {
  password: "password",
  "phone-code": "phone",
  "email-code": "email",
};

export const providerTitle: Record<string, string> = {
  yandex: "Яндекс ID",
  esia: "Госуслуги",
};

/** «1 ключ», «2 ключа», «5 ключей». */
export function keysCount(count: number): string {
  const last = count % 10;
  const tens = count % 100;
  const word =
    last === 1 && tens !== 11
      ? "ключ"
      : last >= 2 && last <= 4 && (tens < 12 || tens > 14)
        ? "ключа"
        : "ключей";
  return `${count} ${word}`;
}
