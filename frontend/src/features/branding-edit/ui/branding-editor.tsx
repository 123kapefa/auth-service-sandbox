import { zodResolver } from "@hookform/resolvers/zod";
import { type ChangeEvent, useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import {
  type Branding,
  BRANDING_LOGO_MAX_BYTES,
  BRANDING_LOGO_TYPES,
  BRANDING_THEMES,
  LoginPreview,
  readAsDataUrl,
} from "@/entities/branding";
import { Button, FieldError, Input, Label } from "@/shared/ui";
import { type BrandingForm as Form, brandingSchema, fromForm, toForm } from "../model/schemas";
import { useSaveBranding } from "../model/use-save-branding";

const segment =
  "flex h-10 cursor-pointer items-center justify-center border border-primary px-3 text-sm has-checked:bg-primary has-checked:font-semibold has-checked:text-primary-foreground has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-ring";

/**
 * Оформление проекта: слева — название, логотип, цвет, тема; справа — страница входа в миниатюре (при теме «как в
 * системе» — светлая и тёмная). Сохранили — страницы входа подхватят при следующем открытии, без пересборки.
 */
export function BrandingEditor({ branding }: { branding: Branding }) {
  const mutation = useSaveBranding();
  const [logoError, setLogoError] = useState<string>();
  const form = useForm<Form>({
    resolver: zodResolver(brandingSchema),
    defaultValues: toForm(branding),
  });
  const values = useWatch({ control: form.control }) as Form;
  const preview = fromForm({
    ...values,
    color: /^#[0-9a-fA-F]{6}$/.test(values.color) ? values.color : "#3c3c3c",
  });
  const { errors, isDirty } = form.formState;

  const pickLogo = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) {
      return;
    }

    if (!BRANDING_LOGO_TYPES.includes(file.type)) {
      setLogoError("Нужна картинка PNG, JPEG, WebP или SVG");
      return;
    }

    if (file.size > BRANDING_LOGO_MAX_BYTES) {
      setLogoError("Логотип — не больше 100 КБ");
      return;
    }

    setLogoError(undefined);
    form.setValue("logo", await readAsDataUrl(file), { shouldDirty: true });
  };

  const submit = form.handleSubmit((form) =>
    mutation.mutate(fromForm(form), { onSuccess: (saved) => reset(saved) }),
  );
  const reset = (to: Branding) => {
    setLogoError(undefined);
    form.reset(toForm(to));
  };

  return (
    <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
      <form onSubmit={submit} className="grid content-start gap-5" noValidate>
        <div className="grid gap-1">
          <Label htmlFor="branding-name">Название проекта</Label>
          <Input
            id="branding-name"
            aria-invalid={errors.name ? true : undefined}
            aria-describedby="branding-name-error"
            {...form.register("name")}
          />
          <FieldError id="branding-name-error" message={errors.name?.message} />
        </div>

        <fieldset className="grid gap-2">
          <legend className="mb-1 font-mono text-[11px] tracking-[0.1em] text-muted-foreground uppercase">
            Логотип
          </legend>
          <div className="flex flex-wrap items-center gap-3">
            <div className="grid size-16 place-items-center border border-border bg-background">
              {values.logo ? (
                <img src={values.logo} alt="Логотип" className="max-h-14 max-w-14 object-contain" />
              ) : (
                <span className="font-mono text-[10px] text-muted-foreground">нет</span>
              )}
            </div>
            <Button asChild variant="outline" size="sm">
              <label htmlFor="branding-logo" className="cursor-pointer">
                {values.logo ? "Заменить" : "Загрузить"}
              </label>
            </Button>
            <input
              id="branding-logo"
              type="file"
              accept={BRANDING_LOGO_TYPES.join(",")}
              aria-describedby="branding-logo-hint branding-logo-error"
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
                Убрать
              </Button>
            )}
          </div>
          <p id="branding-logo-hint" className="text-xs text-muted-foreground">
            PNG, JPEG, WebP или SVG до 100 КБ. Показывается рядом с названием над формой входа.
          </p>
          <FieldError id="branding-logo-error" message={logoError} />
        </fieldset>

        <fieldset className="grid gap-2">
          <legend className="mb-1 font-mono text-[11px] tracking-[0.1em] text-muted-foreground uppercase">
            Цвет
          </legend>
          <div className="grid grid-cols-2 gap-2 sm:max-w-sm">
            <label className={segment}>
              <input
                type="radio"
                className="sr-only"
                checked={!values.hasColor}
                onChange={() => form.setValue("hasColor", false, { shouldDirty: true })}
              />
              Серый
            </label>
            <label className={segment}>
              <input
                type="radio"
                className="sr-only"
                checked={values.hasColor}
                onChange={() => form.setValue("hasColor", true, { shouldDirty: true })}
              />
              Свой цвет
            </label>
          </div>
          {values.hasColor && (
            <div className="flex items-center gap-2">
              <input
                type="color"
                aria-label="Выбрать цвет"
                value={
                  /^#[0-9a-fA-F]{6}$/.test(values.color) ? values.color.toLowerCase() : "#3c3c3c"
                }
                onChange={(event) =>
                  form.setValue("color", event.target.value, {
                    shouldDirty: true,
                    shouldValidate: true,
                  })
                }
                className="h-10 w-12 cursor-pointer border border-border bg-background p-1"
              />
              <Input
                aria-label="Цвет в виде #rrggbb"
                aria-invalid={errors.color ? true : undefined}
                aria-describedby="branding-color-error"
                className="w-32 font-mono"
                {...form.register("color")}
              />
            </div>
          )}
          {values.hasColor && (
            <FieldError id="branding-color-error" message={errors.color?.message} />
          )}
          <p className="text-xs text-muted-foreground">
            Кнопки и отметки на страницах входа. Текст на кнопке — тёмный или светлый, по яркости
            цвета.
          </p>
        </fieldset>

        <fieldset className="grid gap-2">
          <legend className="mb-1 font-mono text-[11px] tracking-[0.1em] text-muted-foreground uppercase">
            Тема страниц входа
          </legend>
          <div className="grid grid-cols-3 gap-2 sm:max-w-md">
            {BRANDING_THEMES.map((theme) => (
              <label key={theme.value} className={segment}>
                <input
                  type="radio"
                  value={theme.value}
                  className="sr-only"
                  {...form.register("theme")}
                />
                {theme.label}
              </label>
            ))}
          </div>
        </fieldset>

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
          Как будет выглядеть
        </span>
        <div className={preview.theme === "system" ? "grid gap-3 sm:grid-cols-2" : "grid"}>
          {preview.theme !== "dark" && <LoginPreview branding={preview} scheme="light" />}
          {preview.theme !== "light" && <LoginPreview branding={preview} scheme="dark" />}
        </div>
      </section>
    </div>
  );
}
