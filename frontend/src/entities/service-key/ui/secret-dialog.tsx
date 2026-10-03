import { useState } from "react";
import { notifyError } from "@/shared/lib/notify";
import { Button, Dialog } from "@/shared/ui";
import { type IssuedServiceKey } from "../types";

/**
 * Ключ сервиса после выдачи или перевыпуска: client_id и секрет. Секрет больше нигде не показывается — его надо сразу
 * сохранить в настройки сервиса (переменная окружения, хранилище секретов). Закрыли окно — только перевыпуск, поэтому
 * окно закрывается только кнопкой «Я сохранил ключ» (не кликом по фону и не Esc).
 */
export function SecretDialog({
  issued,
  onClose,
}: {
  issued: IssuedServiceKey | null;
  onClose: () => void;
}) {
  const [copied, setCopied] = useState(false);
  if (!issued) {
    return null;
  }

  const copy = async () => {
    try {
      // Буфер обмена — только в безопасном контексте (https или localhost) и с разрешения браузера.
      await navigator.clipboard.writeText(issued.clientSecret);
      setCopied(true);
    } catch (error) {
      notifyError("скопировать секрет — выделите его и скопируйте вручную", error);
    }
  };

  return (
    <Dialog
      open
      dismissible={false}
      onOpenChange={(open) => {
        if (!open) {
          setCopied(false);
          onClose();
        }
      }}
      title={`Ключ сервиса «${issued.service.name}»`}
      description="Секрет показывается один раз: сохраните его в настройки сервиса. Потерян — перевыпустите ключ."
    >
      <dl className="grid gap-3 text-sm">
        <div>
          <dt className="text-muted-foreground">client_id</dt>
          <dd className="font-mono">{issued.service.clientId}</dd>
        </div>
        <div>
          <dt className="text-muted-foreground">Секрет</dt>
          <dd className="break-all rounded-md bg-muted p-2 font-mono" data-testid="service-secret">
            {issued.clientSecret}
          </dd>
        </div>
      </dl>
      <div className="mt-4 flex justify-end gap-2">
        <Button variant="outline" onClick={() => void copy()}>
          {copied ? "Скопировано" : "Скопировать секрет"}
        </Button>
        <Button
          onClick={() => {
            setCopied(false);
            onClose();
          }}
        >
          Я сохранил ключ
        </Button>
      </div>
    </Dialog>
  );
}
