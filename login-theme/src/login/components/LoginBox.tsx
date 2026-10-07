import { formatLogin } from "../lib/phone";

/**
 * Что ввели на шаге 1 (или куда ушёл код) — в рамке, моноширинным, номер маской; справа «Изменить». «Изменить» — либо
 * кнопка своей пустой формы (formId: Enter в поле пароля жмёт первую кнопку формы — это должна быть «Войти»), либо
 * ссылка (href) — начать вход заново.
 */
export function LoginBox({
  login,
  label,
  formId,
  href,
  name = "changeLogin",
}: {
  login: string;
  label: string;
  formId?: string;
  href?: string;
  /** Имя кнопки «Изменить» (formId): что прочтёт сервер. */
  name?: string;
}) {
  const action = "shrink-0 text-sm underline underline-offset-4 hover:text-muted-foreground";
  return (
    <div className="flex items-center justify-between gap-3 border border-border bg-background px-3 py-2.5">
      <span id="platform-login-display" className="min-w-0 truncate font-mono text-sm">
        {formatLogin(login)}
      </span>
      {formId !== undefined ? (
        <button
          id="platform-login-change"
          name={name}
          type="submit"
          form={formId}
          formNoValidate
          className={action}
        >
          {label}
        </button>
      ) : (
        href !== undefined && (
          <a id="platform-login-change" href={href} className={action}>
            {label}
          </a>
        )
      )}
    </div>
  );
}
