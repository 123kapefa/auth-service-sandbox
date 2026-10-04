import { Button } from "./button";

interface PagerProps {
  page: number;
  hasMore: boolean;
  onPage: (page: number) => void;
}

/** Страницы без общего числа (сервис отдаёт hasMore): «назад» и «дальше». */
export function Pager({ page, hasMore, onPage }: PagerProps) {
  if (page === 1 && !hasMore) {
    return null;
  }

  return (
    <div className="mt-3 flex items-center gap-3 text-sm">
      <Button size="sm" variant="outline" disabled={page <= 1} onClick={() => onPage(page - 1)}>
        ← Назад
      </Button>
      <span className="font-mono text-[13px] text-muted-foreground">Страница {page}</span>
      <Button size="sm" variant="outline" disabled={!hasMore} onClick={() => onPage(page + 1)}>
        Дальше →
      </Button>
    </div>
  );
}
