import { useEffect, useState } from "react";

/** Обратный отсчёт в секундах (раз в секунду до нуля): сколько ждать до повторной отправки кода. */
export function useCountdown(seconds: number): number {
  const [left, setLeft] = useState(Math.max(0, seconds));

  useEffect(() => {
    if (left <= 0) {
      return undefined;
    }

    const timer = setTimeout(() => setLeft((value) => Math.max(0, value - 1)), 1000);
    return () => clearTimeout(timer);
  }, [left]);

  return left;
}
