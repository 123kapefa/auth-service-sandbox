import { useEffect } from "react";
import { useSearchParams } from "react-router";
import { toast } from "sonner";
import { ACTION_PARAM, ACTION_STATUS_PARAM, actionNotice } from "./status";

const ACTION_TOAST_ID = "account-action";

/** После возврата со страницы действия: уведомление об итоге и чистый адрес (обновление страницы не повторит его). */
export function useActionStatus(onDone: () => void): void {
  const [params, setParams] = useSearchParams();
  const status = params.get(ACTION_STATUS_PARAM);
  const action = params.get(ACTION_PARAM);

  useEffect(() => {
    if (status === null) {
      return;
    }

    // Один id — одно уведомление, даже если эффект сработал дважды (StrictMode в dev), пока адрес не очищен.
    const notice = actionNotice(action, status);
    if (notice?.kind === "success") {
      toast.success(notice.text, { id: ACTION_TOAST_ID });
      onDone();
    } else if (notice?.kind === "error") {
      toast.error(notice.text, { id: ACTION_TOAST_ID });
    }

    setParams(
      (current) => {
        current.delete(ACTION_STATUS_PARAM);
        current.delete(ACTION_PARAM);
        return current;
      },
      { replace: true },
    );
  }, [status, action, setParams, onDone]);
}
