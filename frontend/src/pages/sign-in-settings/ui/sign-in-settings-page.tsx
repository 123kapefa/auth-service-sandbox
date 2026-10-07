import { useQuery } from "@tanstack/react-query";
import { signInSettingsQueries } from "@/entities/sign-in-settings";
import { CodeSettingsEditor } from "@/features/code-settings-edit";
import { ProjectMethodsCard } from "@/features/project-sign-in-edit";
import { getErrorMessage } from "@/shared/api";
import { ErrorState, Loading } from "@/shared/ui";
import { UsageCard } from "./usage-card";

/**
 * «Вход и коды» (админ проекта, экраны 15 и 22 этапа 10): способы входа проекта, расход кодов за сегодня, каналы и
 * защита от накрутки. Каждый блок грузится сам: нет плагина или прав у одного — остальные работают.
 */
export function SignInSettingsPage() {
  const methods = useQuery(signInSettingsQueries.methods());
  const usage = useQuery(signInSettingsQueries.usage());
  const codes = useQuery(signInSettingsQueries.codes());

  return (
    <div className="grid gap-4">
      <h1 className="text-3xl font-bold tracking-tight">Вход и коды</h1>
      {methods.isPending ? (
        <Loading />
      ) : methods.isError ? (
        <ErrorState
          message={getErrorMessage(methods.error, "Не удалось загрузить способы входа")}
        />
      ) : (
        <ProjectMethodsCard settings={methods.data} />
      )}
      {usage.isPending ? (
        <Loading />
      ) : usage.isError ? (
        <ErrorState message={getErrorMessage(usage.error, "Не удалось загрузить расход кодов")} />
      ) : (
        <UsageCard usage={usage.data} />
      )}
      {codes.isPending ? (
        <Loading />
      ) : codes.isError ? (
        <ErrorState
          message={getErrorMessage(codes.error, "Не удалось загрузить настройки кодов")}
        />
      ) : (
        <CodeSettingsEditor
          key={JSON.stringify(codes.data)}
          settings={codes.data}
          methods={methods.data}
        />
      )}
    </div>
  );
}
