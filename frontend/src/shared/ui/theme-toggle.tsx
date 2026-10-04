import { useState } from "react";
import { currentTheme, setTheme } from "@/shared/lib/theme";
import { Button } from "./button";

/** Переключатель светлой и тёмной темы: квадрат, наполовину залитый. */
export function ThemeToggle() {
  const [theme, setState] = useState(currentTheme);
  const next = theme === "dark" ? "light" : "dark";

  return (
    <Button
      variant="outline"
      size="icon"
      aria-label={next === "dark" ? "Тёмная тема" : "Светлая тема"}
      title={next === "dark" ? "Тёмная тема" : "Светлая тема"}
      onClick={() => {
        setTheme(next);
        setState(next);
      }}
    >
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="square"
        aria-hidden
      >
        <rect x="4" y="4" width="16" height="16" />
        <path d="M12 4h8v16h-8z" fill="currentColor" />
      </svg>
    </Button>
  );
}
