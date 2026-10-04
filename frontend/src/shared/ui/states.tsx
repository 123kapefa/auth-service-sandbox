import { type ReactNode } from "react";

export function EmptyState({ children }: { children: ReactNode }) {
  return <p className="py-6 text-center text-sm text-muted-foreground">{children}</p>;
}

export function ErrorState({ message }: { message: string }) {
  return (
    <p
      role="alert"
      className="flex items-center gap-3 bg-primary p-4 text-sm text-primary-foreground"
    >
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
