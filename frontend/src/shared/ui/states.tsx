import { type ReactNode } from "react";

export function EmptyState({ children }: { children: ReactNode }) {
  return <p className="py-6 text-center text-sm text-muted-foreground">{children}</p>;
}

export function ErrorState({ message }: { message: string }) {
  return (
    <p
      role="alert"
      className="rounded-md border border-destructive/40 p-4 text-sm text-destructive"
    >
      {message}
    </p>
  );
}

export function Loading() {
  return <p className="py-6 text-sm text-muted-foreground">Загрузка…</p>;
}
