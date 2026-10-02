import { type ComponentProps } from "react";
import { cn } from "@/shared/lib/cn";

export function Badge({ className, ...props }: ComponentProps<"span">) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full border border-border px-2 py-0.5 text-xs font-medium",
        className,
      )}
      {...props}
    />
  );
}
