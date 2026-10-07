import { type CodeUsage } from "@/entities/sign-in-settings";
import { cn } from "@/shared/lib/cn";
import { formatCount, formatRubles } from "@/shared/lib/format";
import { Badge, Card, CardTitle } from "@/shared/ui";

/** Ниже этой доли введённых кодов — признак накрутки (оповещение, 10.1). */
const VERIFIED_ALERT = 60;

/** «Сегодня» (экран 15 этапа 10): звонки, SMS, письма, доля введённых кодов и бюджет дня. Обновляется раз в минуту. */
export function UsageCard({ usage }: { usage: CodeUsage }) {
  const { budget } = usage;
  const percent = budget.limit > 0 ? Math.min(100, (budget.spent / budget.limit) * 100) : 0;
  const smsShare = usage.sent > 0 ? Math.round((usage.sms.sent / usage.sent) * 100) : null;

  return (
    <Card aria-label="Сегодня">
      <CardTitle className="mb-1">Сегодня</CardTitle>
      <p className="text-sm text-muted-foreground">
        Расход за сутки по {usage.zone === "Europe/Moscow" ? "московскому времени" : usage.zone}.
        Цены — из договора с провайдером.
      </p>
      <dl className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Tile
          label="Звонки"
          value={formatCount(usage.call.sent)}
          note={`≈ ${formatRubles(usage.call.cost)}`}
        />
        <Tile
          label="SMS"
          value={formatCount(usage.sms.sent)}
          note={`≈ ${formatRubles(usage.sms.cost)}${smsShare === null ? "" : ` · ${smsShare} % от кодов`}`}
        />
        <Tile label="Почта" value={formatCount(usage.email.sent)} note="бесплатно" />
        <Tile
          label="Введено кодов"
          value={usage.verifiedPercent === null ? "—" : `${usage.verifiedPercent} %`}
          note={`ниже ${VERIFIED_ALERT} % — оповещение`}
          alert={usage.verifiedPercent !== null && usage.verifiedPercent < VERIFIED_ALERT}
        />
      </dl>
      <div className="mt-4 flex flex-wrap items-center justify-between gap-x-4 gap-y-1 text-sm">
        <p>
          Бюджет дня:{" "}
          {budget.limit > 0 ? (
            <strong>
              {formatRubles(budget.spent)} из {formatRubles(budget.limit)}
            </strong>
          ) : (
            <>
              <strong>{formatRubles(budget.spent)}</strong> · без ограничения
            </>
          )}
          {budget.exhausted && (
            <Badge variant="warning" className="ml-2">
              SMS выключены до полуночи
            </Badge>
          )}
        </p>
        {budget.limit > 0 && (
          <p className="text-muted-foreground">
            при {budget.alertPercent} % — письмо админам, при 100 % — SMS выключаются до полуночи
          </p>
        )}
      </div>
      {budget.limit > 0 && (
        <div
          role="progressbar"
          aria-label="Бюджет дня"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(percent)}
          className="mt-2 h-2 border border-primary bg-background"
        >
          <div
            className={cn("h-full bg-primary", budget.exhausted && "bg-hatch")}
            style={{ width: `${percent}%` }}
          />
        </div>
      )}
    </Card>
  );
}

function Tile({
  label,
  value,
  note,
  alert = false,
}: {
  label: string;
  value: string;
  note: string;
  alert?: boolean;
}) {
  return (
    <div
      className={cn(
        "border border-border bg-background p-3",
        alert && "border-dashed border-primary bg-hatch",
      )}
    >
      <dt className="font-mono text-[11px] tracking-[0.1em] text-muted-foreground uppercase">
        {label}
      </dt>
      <dd className="mt-1 text-2xl font-bold">{value}</dd>
      <dd className="text-xs text-muted-foreground">{note}</dd>
    </div>
  );
}
