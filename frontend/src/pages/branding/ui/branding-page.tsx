import { useQuery } from "@tanstack/react-query";
import { brandingQueries } from "@/entities/branding";
import { BrandingEditor } from "@/features/branding-edit";
import { getErrorMessage } from "@/shared/api";
import { Card, CardTitle, ErrorState, Loading } from "@/shared/ui";

/** Оформление проекта (админ проекта): название, логотип, цвет и тема страниц входа. */
export function BrandingPage() {
  const branding = useQuery(brandingQueries.current());

  if (branding.isPending) {
    return <Loading />;
  }

  if (branding.isError) {
    return (
      <ErrorState message={getErrorMessage(branding.error, "Не удалось загрузить оформление")} />
    );
  }

  return (
    <Card>
      <CardTitle className="mb-0">Оформление</CardTitle>
      <p className="mb-5 text-sm text-muted-foreground">
        Страницы входа проекта: вход, код, смена пароля, 2FA. Сохранили — посетители увидят при
        следующем открытии страницы, пересобирать ничего не нужно.
      </p>
      <BrandingEditor key={JSON.stringify(branding.data)} branding={branding.data} />
    </Card>
  );
}
