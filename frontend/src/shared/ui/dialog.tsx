import * as DialogPrimitive from "@radix-ui/react-dialog";
import { type ReactNode } from "react";
import { Button } from "./button";

interface DialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: string;
  children: ReactNode;
  /** false — не закрывается кликом по фону и Esc: только своими кнопками (например, окно с одноразовым секретом). */
  dismissible?: boolean;
}

/** Модальное окно (Radix Dialog): фокус внутри, Esc закрывает (если dismissible). */
export function Dialog({
  open,
  onOpenChange,
  title,
  description,
  children,
  dismissible = true,
}: DialogProps) {
  const keepOpen = (event: Event) => {
    if (!dismissible) {
      event.preventDefault();
    }
  };

  return (
    <DialogPrimitive.Root open={open} onOpenChange={onOpenChange}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 bg-[#1c1c1c]/50" />
        <DialogPrimitive.Content
          onInteractOutside={keepOpen}
          onEscapeKeyDown={keepOpen}
          className="fixed top-1/2 left-1/2 w-[min(28rem,calc(100vw-2rem))] -translate-x-1/2 -translate-y-1/2 border-2 border-primary bg-background p-6 text-foreground shadow-hard-lg"
        >
          <DialogPrimitive.Title className="mb-1 text-lg font-bold">{title}</DialogPrimitive.Title>
          {description ? (
            <DialogPrimitive.Description className="mb-4 text-sm text-muted-foreground">
              {description}
            </DialogPrimitive.Description>
          ) : (
            <DialogPrimitive.Description className="sr-only">{title}</DialogPrimitive.Description>
          )}
          {children}
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}

interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  confirmLabel: string;
  destructive?: boolean;
  pending?: boolean;
  onConfirm: () => void;
}

/** Подтверждение необратимого или заметного действия. */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  destructive = false,
  pending = false,
  onConfirm,
}: ConfirmDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange} title={title} description={description}>
      <div className="flex justify-end gap-2">
        <Button variant="outline" onClick={() => onOpenChange(false)}>
          Отмена
        </Button>
        <Button
          variant={destructive ? "destructive" : "default"}
          disabled={pending}
          onClick={onConfirm}
        >
          {confirmLabel}
        </Button>
      </div>
    </Dialog>
  );
}
