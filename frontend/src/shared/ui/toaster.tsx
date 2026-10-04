import { Toaster as SonnerToaster } from "sonner";

/** Уведомления в стиле интерфейса: залитый блок с жёсткой тенью. */
export function Toaster() {
  return (
    <SonnerToaster
      position="top-right"
      toastOptions={{
        unstyled: true,
        classNames: {
          toast:
            "flex w-full items-center gap-3 bg-primary px-4 py-3 font-sans text-sm text-primary-foreground shadow-[6px_6px_0_var(--color-shadow)]",
          title: "font-medium",
          description: "opacity-80",
          actionButton: "border border-primary-foreground px-2 py-1 text-xs",
          cancelButton: "px-2 py-1 text-xs opacity-80",
        },
      }}
    />
  );
}
