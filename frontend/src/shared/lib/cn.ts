import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

/** Классы Tailwind: условные и без конфликтов (как cn() в shadcn/ui). */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}
