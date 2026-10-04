import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";
import { type ComponentProps } from "react";
import { cn } from "@/shared/lib/cn";

const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 border text-sm font-medium whitespace-nowrap transition-[background-color,transform,box-shadow] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:pointer-events-none disabled:opacity-50",
  {
    variants: {
      variant: {
        // Основная: заливка и жёсткая тень; при нажатии кнопка «садится» на тень.
        default:
          "border-primary bg-primary font-semibold text-primary-foreground shadow-hard hover:bg-primary/90 active:translate-x-1 active:translate-y-1 active:shadow-none",
        outline: "border-primary bg-transparent hover:bg-muted",
        ghost: "border-transparent hover:bg-muted",
        // Опасное действие — пунктир и штриховка, не красный.
        destructive: "border-dashed border-primary bg-hatch hover:bg-muted",
      },
      size: {
        default: "h-10 px-4",
        sm: "h-8 px-3 text-[13px]",
        icon: "size-10",
      },
    },
    defaultVariants: { variant: "default", size: "default" },
  },
);

type ButtonProps = ComponentProps<"button"> &
  VariantProps<typeof buttonVariants> & { asChild?: boolean };

export function Button({ className, variant, size, asChild = false, ...props }: ButtonProps) {
  const Component = asChild ? Slot : "button";
  return <Component className={cn(buttonVariants({ variant, size }), className)} {...props} />;
}
