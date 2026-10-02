import { useState } from "react";
import { formatLogin, nextLogin, toLogin } from "../lib/phone";

/**
 * Поле «Телефон или почта»: номер показывается маской +7 (999) 000-00-01 (с 8 и 10 цифр — тоже +7), на сервер уходит
 * цифрами скрытым полем username; почта — как есть. Нужно везде, где Keycloak ищет пользователя по логину: Keycloak
 * не узнает «8 999 000-00-01», а логин хранится цифрами.
 */
export function LoginInput({
  className,
  defaultValue,
  autoComplete,
  invalid,
  describedBy,
}: {
  className: string;
  defaultValue: string;
  autoComplete: string;
  invalid: boolean;
  describedBy: string;
}) {
  const [value, setValue] = useState(() => formatLogin(defaultValue));
  return (
    <>
      <input type="hidden" name="username" value={toLogin(value)} />
      <input
        id="username"
        className={className}
        value={value}
        onChange={(event) => setValue(nextLogin(value, event.target.value))}
        type="text"
        autoFocus
        autoComplete={autoComplete}
        aria-invalid={invalid || undefined}
        aria-describedby={invalid ? describedBy : undefined}
      />
    </>
  );
}
