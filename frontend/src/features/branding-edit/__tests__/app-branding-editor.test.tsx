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

const PROJECT = { name: "Магазин", logo: null, color: "#1f6feb", theme: "light" } as const;
const AS_PROJECT = {
  clientId: "manager-1c",
  name: null,
  logo: null,
  color: null,
  project: PROJECT,
};

describe("оформление приложения — как на сервере", () => {
  it("пустое название и «как у проекта» — null; свой цвет — строчными", () => {
    expect(fromAppForm(toAppForm(AS_PROJECT, PROJECT.color))).toEqual({
      name: null,
      logo: null,
      color: null,
    });
    expect(
      fromAppForm({ name: " Менеджер ", logo: null, hasColor: true, color: "#AA3300" }),
    ).toEqual({ name: "Менеджер", logo: null, color: "#aa3300" });
    expect(
      appBrandingSchema.safeParse({ name: "", logo: null, hasColor: true, color: "red" }).success,
    ).toBe(false);
    expect(
      appBrandingSchema.safeParse({ name: "x".repeat(61), logo: null, hasColor: false, color: "" })
        .success,
    ).toBe(false);
  });

  it("итог — проект, поверх своё; тема — проекта", () => {
    expect(effectiveBranding(PROJECT, { name: "Менеджер", logo: null, color: null })).toEqual({
      ...PROJECT,
      name: "Менеджер",
    });
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
    expect(screen.getByLabelText("Цвет в виде #rrggbb")).toHaveValue("#1f6feb");
    await user.clear(screen.getByLabelText("Цвет в виде #rrggbb"));
    await user.type(screen.getByLabelText("Цвет в виде #rrggbb"), "#AA3300");

    expect(screen.getByRole("figure", { name: /светлая тема/ })).toHaveTextContent("Менеджер 1С");
    expect(screen.queryByRole("figure", { name: /тёмная тема/ })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(saveApp).toHaveBeenCalledWith("manager-1c", {
      name: "Менеджер 1С",
      logo: null,
      color: "#aa3300",
    });
  });
});
