import { Button, Card } from "@/shared/ui";

/** Вошёл, но управлять нечем: покупатель или сотрудник не в том режиме. */
export function NoAccessPage({ activeMode }: { activeMode: string | null }) {
  return (
    <div className="mx-auto max-w-lg px-4 py-16">
      <Card>
        <h1 className="mb-3 text-2xl font-bold tracking-tight">Нет доступа к админке</h1>
        <p className="mb-4 text-sm text-muted-foreground">
          В режиме «{activeMode ?? "—"}» прав на управление пользователями, ролями или журналом нет.
          Если вы сотрудник — переключитесь в рабочий режим и обновите страницу.
        </p>
        <Button asChild variant="outline">
          <a href="/">На главную</a>
        </Button>
      </Card>
    </div>
  );
}
