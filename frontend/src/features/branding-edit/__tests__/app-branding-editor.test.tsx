import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as BrandingModule from "@/entities/branding";
import { effectiveBranding } from "@/entities/branding";
import { renderWithProviders } from "@/test/render";
import { appBrandingSchema, fromAppForm, toAppForm } from "../model/app-schemas";
import { AppBrandingEditor } from "../ui/app-branding-editor";

const saveApp = vi.hoisted(() => vi.fn());
vi.mock("@/entities/branding", async (original) => {
  const actual = await original<typeof BrandingModule>();
  return { ...actual, brandingApi: { ...actual.brandingApi, saveApp } };
});

const PROJECT = {
  name: "Магазин",
  logo: null,
  color: "#1f6feb",
  colorDark: "#7aa7ff",
  theme: "light",
} as const;
const AS_PROJECT = {
  clientId: "manager-1c",
  name: null,
  logo: null,
  color: null,
  colorDark: null,
  project: PROJECT,
};

describe("оформление приложения — как на сервере", () => {
  it("пустое название и «как у проекта» — null; свой цвет — строчными", () => {
    const form = toAppForm(AS_PROJECT, PROJECT.color);
    expect(fromAppForm(form)).toEqual({ name: null, logo: null, color: null, colorDark: null });
    expect(fromAppForm({ ...form, name: " Менеджер ", hasColor: true, color: "#AA3300" })).toEqual({
      name: "Менеджер",
      logo: null,
      color: "#aa3300",
      colorDark: null,
    });
    expect(appBrandingSchema.safeParse({ ...form, hasColor: true, color: "red" }).success).toBe(
      false,
    );
    expect(appBrandingSchema.safeParse({ ...form, name: "x".repeat(61) }).success).toBe(false);
  });

  it("итог — проект, поверх своё; тема — проекта; свой акцент — парой", () => {
    const own = { name: "Менеджер", logo: null, color: null, colorDark: null };
    expect(effectiveBranding(PROJECT, own)).toEqual({ ...PROJECT, name: "Менеджер" });
    // Свой цвет без своего тёмного — тот же и в тёмной теме, не тёмный цвет проекта.
    expect(effectiveBranding(PROJECT, { ...own, color: "#aa3300" })).toMatchObject({
      color: "#aa3300",
      colorDark: null,
    });
    expect(
      effectiveBranding(PROJECT, { ...own, color: "#aa3300", colorDark: "#ff8866" }),
    ).toMatchObject({ color: "#aa3300", colorDark: "#ff8866" });
  });
});

describe("AppBrandingEditor", () => {
  beforeEach(() =>
    saveApp
      .mockReset()
      .mockImplementation((clientId: string, own: object) =>
        Promise.resolve({ ...AS_PROJECT, ...own, clientId }),
      ),
  );

  it("сохраняет своё название и цвет; миниатюра — с названием приложения", async () => {
    const user = userEvent.setup();
    renderWithProviders(<AppBrandingEditor branding={AS_PROJECT} />);

    expect(screen.getByLabelText("Название приложения")).toHaveAttribute("placeholder", "Магазин");
    expect(screen.getByRole("button", { name: "Сохранить" })).toBeDisabled();

    await user.type(screen.getByLabelText("Название приложения"), "Менеджер 1С");
    await user.click(screen.getByRole("radio", { name: "Свой цвет" }));
    expect(screen.getByLabelText("Светлая тема, #rrggbb")).toHaveValue("#1f6feb");
    await user.clear(screen.getByLabelText("Светлая тема, #rrggbb"));
    await user.type(screen.getByLabelText("Светлая тема, #rrggbb"), "#AA3300");

    expect(screen.getByRole("figure", { name: /светлая тема/ })).toHaveTextContent("Менеджер 1С");
    expect(screen.queryByRole("figure", { name: /тёмная тема/ })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(saveApp).toHaveBeenCalledWith("manager-1c", {
      name: "Менеджер 1С",
      logo: null,
      color: "#aa3300",
      colorDark: null,
    });
  });
});
