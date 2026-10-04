import { type ComponentProps } from "react";
import { cn } from "@/shared/lib/cn";

/** Карточка: рамка, фон темнее страницы, квадрат в левом верхнем углу. */
export function Card({ className, ...props }: ComponentProps<"section">) {
  return (
    <section className={cn("corner-mark border border-border bg-card p-5", className)} {...props} />
  );
}

export function CardTitle({ className, ...props }: ComponentProps<"h2">) {
  return (
    <h2
      className={cn(
        "mb-3 font-mono text-[11px] font-medium tracking-[0.14em] text-muted-foreground uppercase",
        className,
      )}
      {...props}
    />
  );
}
