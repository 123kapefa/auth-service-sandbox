import { type ComponentProps } from "react";
import { cn } from "@/shared/lib/cn";

const field =
  "w-full border border-border bg-background text-sm placeholder:text-muted-foreground focus-visible:border-primary focus-visible:shadow-hard focus-visible:outline-none aria-invalid:border-dashed aria-invalid:border-primary aria-invalid:bg-hatch";

export function Input({ className, ...props }: ComponentProps<"input">) {
  return <input className={cn(field, "h-10 px-3", className)} {...props} />;
}

export function Textarea({ className, ...props }: ComponentProps<"textarea">) {
  return <textarea className={cn(field, "min-h-20 px-3 py-2", className)} {...props} />;
}

export function Label({ className, ...props }: ComponentProps<"label">) {
  return (
    <label
      className={cn(
        "font-mono text-[11px] tracking-[0.1em] text-muted-foreground uppercase",
        className,
      )}
      {...props}
    />
  );
}

export function FieldError({ id, message }: { id: string; message?: string }) {
  return message ? (
    <p
      id={id}
      role="alert"
      className="inline-flex items-center gap-2 self-start bg-danger-muted px-2 py-1 text-[13px] text-danger"
    >
      <span className="font-mono font-bold" aria-hidden>
        !
      </span>
      {message}
    </p>
  ) : null;
}
