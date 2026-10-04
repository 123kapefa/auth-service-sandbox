import { zodResolver } from "@hookform/resolvers/zod";
import { type ChangeEvent, useState } from "react";
import { type UseFormReturn, useForm, useWatch } from "react-hook-form";
import {
  type AppBranding,
  BRANDING_LOGO_TYPES,
  effectiveBranding,
  LoginPreview,
  readLogo,
} from "@/entities/branding";
import { Button, FieldError, Input, Label } from "@/shared/ui";
import { type AccentForm, previewAccent } from "../model/accent";
import {
  type AppBrandingForm as Form,
  appBrandingSchema,
  fromAppForm,
  toAppForm,
} from "../model/app-schemas";
import { useSaveAppBranding } from "../model/use-save-app-branding";
import { AccentFields } from "./accent-fields";

const legend = "mb-1 font-mono text-[11px] tracking-[0.1em] text-muted-foreground uppercase";

/**
 * Оформление приложения поверх проекта: своё название, логотип, акцент — или как у проекта; тема — всегда проекта.
 * Справа — страница входа в приложение в миниатюре.
 */
export function AppBrandingEditor({ branding }: { branding: AppBranding }) {
  const { clientId, project } = branding;
  const mutation = useSaveAppBranding(clientId);
  const [logoError, setLogoError] = useState<string>();
  const form = useForm<Form>({
    resolver: zodResolver(appBrandingSchema),
    defaultValues: toAppForm(branding, project.color),
  });
  const values = useWatch({ control: form.control }) as Form;
  const preview = effectiveBranding(project, fromAppForm({ ...values, ...previewAccent(values) }));
  const { errors, isDirty } = form.formState;

  const pickLogo = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) {
      return;
    }

    const read = await readLogo(file);
    if ("error" in read) {
      setLogoError(read.error);
      return;
    }

    setLogoError(undefined);
    form.setValue("logo", read.logo, { shouldDirty: true });
  };

  const reset = (to: AppBranding) => {
    setLogoError(undefined);
    form.reset(toAppForm(to, to.project.color));
  };
  const submit = form.handleSubmit((form) =>
    mutation.mutate(fromAppForm(form), { onSuccess: (saved) => reset(saved) }),
  );

  return (
    <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
      <form onSubmit={submit} className="grid content-start gap-5" noValidate>
        <div className="grid gap-1">
          <Label htmlFor="app-branding-name">Название приложения</Label>
          <Input
            id="app-branding-name"
            placeholder={project.name}
            aria-invalid={errors.name ? true : undefined}
            aria-describedby="app-branding-name-hint app-branding-name-error"
            {...form.register("name")}
          />
          <p id="app-branding-name-hint" className="text-xs text-muted-foreground">
            Пусто — как у проекта («{project.name}»).
          </p>
          <FieldError id="app-branding-name-error" message={errors.name?.message} />
        </div>

        <fieldset className="grid gap-2">
          <legend className={legend}>Логотип</legend>
          <div className="flex flex-wrap items-center gap-3">
            <div className="grid size-16 place-items-center border border-border bg-background">
              {preview.logo ? (
                <img
                  src={preview.logo}
                  alt="Логотип"
                  className="max-h-14 max-w-14 object-contain"
                />
              ) : (
                <span className="font-mono text-[10px] text-muted-foreground">нет</span>
              )}
            </div>
            <Button asChild variant="outline" size="sm">
              <label htmlFor="app-branding-logo" className="cursor-pointer">
                {values.logo ? "Заменить" : "Свой логотип"}
              </label>
            </Button>
            <input
              id="app-branding-logo"
              type="file"
              accept={BRANDING_LOGO_TYPES.join(",")}
              aria-describedby="app-branding-logo-hint app-branding-logo-error"
              className="sr-only"
              onChange={(event) => void pickLogo(event)}
            />
            {values.logo && (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => form.setValue("logo", null, { shouldDirty: true })}
              >
                Как у проекта
              </Button>
            )}
          </div>
          <p id="app-branding-logo-hint" className="text-xs text-muted-foreground">
            {values.logo
              ? "Свой логотип: PNG, JPEG, WebP или SVG до 100 КБ."
              : project.logo
                ? "Сейчас — логотип проекта."
                : "У проекта логотипа нет."}
          </p>
          <FieldError id="app-branding-logo-error" message={logoError} />
        </fieldset>

        <AccentFields
          form={form as unknown as UseFormReturn<AccentForm>}
          id="app-branding"
          offLabel="Как у проекта"
        />
        <p className="-mt-3 text-xs text-muted-foreground">
          Тема страниц (светлая, тёмная) — общая для проекта, в его «Оформлении».
        </p>

        <div className="flex flex-wrap gap-2">
          <Button type="submit" disabled={mutation.isPending || !isDirty}>
            Сохранить
          </Button>
          <Button
            type="button"
            variant="outline"
            disabled={!isDirty}
            onClick={() => reset(branding)}
          >
            Отменить изменения
          </Button>
        </div>
      </form>

      <section aria-label="Как будет выглядеть" className="grid content-start gap-2">
        <span className="font-mono text-[11px] tracking-[0.1em] text-muted-foreground uppercase">
          Вход в приложение
        </span>
        <div className={preview.theme === "system" ? "grid gap-3 sm:grid-cols-2" : "grid"}>
          {preview.theme !== "dark" && <LoginPreview branding={preview} scheme="light" />}
          {preview.theme !== "light" && <LoginPreview branding={preview} scheme="dark" />}
        </div>
      </section>
    </div>
  );
}
