import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { accountDevicesQueries } from "@/entities/account-devices";
import { type SignInMethodKey } from "@/entities/account-security";
import { Button, Dialog } from "@/shared/ui";
import { useSetSignInMethod } from "../model/use-set-sign-in-method";

/** Чем можно будет войти после выключения: «Ключ доступа — iPhone, MacBook», «Код на почту — anna@mail.ru». */
export interface RemainingWay {
  title: string;
  detail: string | null;
}

const titles: Record<SignInMethodKey, string> = {
  password: "Выключить вход по паролю?",
  "phone-code": "Выключить код на телефон?",
  "email-code": "Выключить код на почту?",
};

const texts: Record<SignInMethodKey, string> = {
  password:
    "Войти по паролю не сможет никто, даже вы. Если пароль где-то утёк, он станет бесполезен.",
  "phone-code":
    "Код на номер не придёт никому, даже вам. Если номер перевыпустят или передадут другому человеку, по нему не войдут.",
  "email-code": "Код на почту не придёт никому, даже вам. Почта останется для уведомлений.",
};

const signOutReason: Record<SignInMethodKey, string> = {
  password: "если пароль узнал кто-то ещё, его сессия закроется",
  "phone-code": "если кто-то вошёл по коду на ваш номер, его сессия закроется",
  "email-code": "если кто-то вошёл по коду с вашей почты, его сессия закроется",
};

/**
 * Окно «Выключить …?» (экран 03 этапа 10): что перестанет работать, чем войти останется, «Выйти на других устройствах»
 * (по умолчанию да). Вход был давно — сервис попросит войти заново (уведомление с кнопкой).
 */
export function DisableMethodDialog({
  method,
  remaining,
  onClose,
}: {
  method: SignInMethodKey;
  remaining: RemainingWay[];
  onClose: () => void;
}) {
  const [signOut, setSignOut] = useState(true);
  const mutation = useSetSignInMethod(onClose);
  const { data: sessions } = useQuery(accountDevicesQueries.sessions());
  const others = sessions?.filter((session) => !session.isCurrent).length;

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title={titles[method]}
      description={texts[method]}
    >
      {remaining.length > 0 && (
        <div className="mb-4 text-sm">
          <p className="mb-1.5 text-muted-foreground">Останутся:</p>
          <ul className="grid gap-1">
            {remaining.map((way) => (
              <li key={way.title} className="flex gap-2">
                <span aria-hidden className="mt-[0.45em] size-1.5 flex-none bg-primary" />
                <span>
                  {way.title}
                  {way.detail && ` — ${way.detail}`}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
      <label className="mb-4 flex items-start gap-2.5 text-sm">
        <input
          type="checkbox"
          className="mt-0.5"
          checked={signOut}
          onChange={(event) => setSignOut(event.target.checked)}
        />
        <span>
          Выйти на других устройствах{others ? ` (${others})` : ""} — {signOutReason[method]}
        </span>
      </label>
      <p className="mb-5 text-sm text-muted-foreground">
        Если вы вошли давно, сначала попросим войти заново. Об изменении пришлём письмо или SMS.
      </p>
      <div className="flex flex-wrap gap-2">
        <Button
          disabled={mutation.isPending}
          onClick={() => mutation.mutate({ method, enabled: false, signOutOtherDevices: signOut })}
        >
          Выключить
        </Button>
        <Button variant="outline" onClick={onClose}>
          Отмена
        </Button>
      </div>
    </Dialog>
  );
}
