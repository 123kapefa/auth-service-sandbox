/** Чем можно будет войти после изменения: «Ключ доступа — iPhone, MacBook», «Код на почту — anna@mail.ru». */
export interface RemainingWay {
  title: string;
  detail: string | null;
}

/** «Останутся: …» в окнах «Выключить …?» и «Отвязать …?». */
export function RemainingWays({ ways }: { ways: RemainingWay[] }) {
  if (ways.length === 0) {
    return null;
  }

  return (
    <div className="mb-4 text-sm">
      <p className="mb-1.5 text-muted-foreground">Останутся:</p>
      <ul className="grid gap-1">
        {ways.map((way) => (
          <li key={way.title} className="flex gap-2">
            <span aria-hidden className="mt-[0.45em] size-1.5 flex-none bg-primary" />
            <span>
              {way.title}
              {way.detail && ` — ${way.detail}`}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}
