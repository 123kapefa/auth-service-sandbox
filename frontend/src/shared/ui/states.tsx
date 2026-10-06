import { type ReactNode } from "react";

export function EmptyState({ children }: { children: ReactNode }) {
  return <p className="py-6 text-center text-sm text-muted-foreground">{children}</p>;
}

export function ErrorState({ message }: { message: string }) {
  return (
    <p role="alert" className="flex items-center gap-3 bg-danger-muted p-4 text-sm text-danger">
      <span className="font-mono font-bold" aria-hidden>
        !
      </span>
      {message}
    </p>
  );
}

export function Loading() {
  return <p className="py-6 font-mono text-[13px] text-muted-foreground">Загрузка…</p>;
}

/**
 * Первый экран приложения, пока грузятся код и сессия (сразу после входа): только фон страницы и квадрат по центру.
 * Появляется не сразу — быстрая загрузка не мигает ничем.
 */
export function PageLoading() {
  return (
    <div
      role="status"
      aria-label="Загрузка"
      className="flex min-h-svh animate-appear-late items-center justify-center"
    >
      <span className="size-3 animate-pulse bg-primary motion-reduce:animate-none" />
    </div>
  );
}
