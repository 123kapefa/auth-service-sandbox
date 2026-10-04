import { cva, type VariantProps } from "class-variance-authority";
import { type ComponentProps } from "react";
import { cn } from "@/shared/lib/cn";

const badgeVariants = cva(
  "inline-flex h-[22px] items-center gap-1.5 border px-2 font-mono text-[11px] tracking-wide whitespace-nowrap",
  {
    variants: {
      variant: {
        default: "border-border bg-background",
        // Включено, подтверждено, встроенное — залитая метка.
        solid: "border-primary bg-primary text-primary-foreground uppercase",
        // Закрыто, заблокировано, не подтверждено — пунктир и штриховка.
        warning: "border-dashed border-primary bg-hatch uppercase",
      },
    },
    defaultVariants: { variant: "default" },
  },
);

export function Badge({
  className,
  variant,
  ...props
}: ComponentProps<"span"> & VariantProps<typeof badgeVariants>) {
  return <span className={cn(badgeVariants({ variant }), className)} {...props} />;
}
