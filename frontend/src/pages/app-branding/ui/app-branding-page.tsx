import { useQuery } from "@tanstack/react-query";
import { useParams } from "react-router";
import { brandingQueries } from "@/entities/branding";
import { AppBrandingEditor } from "@/features/branding-edit";
import { getErrorMessage } from "@/shared/api";
import { Card, CardTitle, ErrorState, Loading } from "@/shared/ui";

/** Оформление приложения (админ проекта): своё название, логотип, цвет поверх оформления проекта. */
export function AppBrandingPage() {
  const { clientId = "" } = useParams();
  const branding = useQuery(brandingQueries.app(clientId));

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
        Страницы входа в приложение, его письма (сброс пароля, подтверждение почты, код входа) и
        личный кабинет, открытый из него. Что не задано — как у проекта.
      </p>
      <AppBrandingEditor key={JSON.stringify(branding.data)} branding={branding.data} />
    </Card>
  );
}
