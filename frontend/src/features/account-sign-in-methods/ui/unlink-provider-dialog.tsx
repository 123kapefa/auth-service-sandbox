import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { accountDevicesQueries } from "@/entities/account-devices";
import { Button, Dialog } from "@/shared/ui";
import { providerTitle } from "../model/labels";
import { useUnlinkProvider } from "../model/use-unlink-provider";
import { type RemainingWay, RemainingWays } from "./remaining-ways";

/**
 * Окно «Отвязать Яндекс ID?»: войти через провайдера будет нельзя, что останется, «Выйти на других устройствах» (по
 * умолчанию да). Последний способ входа отвязать нельзя — кнопка на странице выключена, сервер проверяет ещё раз.
 */
export function UnlinkProviderDialog({
  provider,
  remaining,
  onClose,
}: {
  provider: string;
  remaining: RemainingWay[];
  onClose: () => void;
}) {
  const [signOut, setSignOut] = useState(true);
  const mutation = useUnlinkProvider(onClose);
  const { data: sessions } = useQuery(accountDevicesQueries.sessions());
  const others = sessions?.filter((session) => !session.isCurrent).length;
  const title = providerTitle[provider] ?? provider;

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title={`Отвязать ${title}?`}
      description={`Войти через ${title} в этот аккаунт будет нельзя. Привязать снова можно на странице входа.`}
    >
      <RemainingWays ways={remaining} />
      <label className="mb-4 flex items-start gap-2.5 text-sm">
        <input
          type="checkbox"
          className="mt-0.5"
          checked={signOut}
          onChange={(event) => setSignOut(event.target.checked)}
        />
        <span>
          Выйти на других устройствах{others ? ` (${others})` : ""} — если кто-то вошёл через{" "}
          {title}, его сессия закроется
        </span>
      </label>
      <p className="mb-5 text-sm text-muted-foreground">
        Если вы вошли давно, сначала попросим войти заново. Об изменении пришлём письмо или SMS.
      </p>
      <div className="flex flex-wrap gap-2">
        <Button
          disabled={mutation.isPending}
          onClick={() => mutation.mutate({ provider, signOutOtherDevices: signOut })}
        >
          Отвязать
        </Button>
        <Button variant="outline" onClick={onClose}>
          Отмена
        </Button>
      </div>
    </Dialog>
  );
}
