import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as BrandingModule from "@/entities/branding";
import { renderWithProviders } from "@/test/render";
import { brandingSchema, fromForm, toForm } from "../model/schemas";
import { BrandingEditor } from "../ui/branding-editor";

const save = vi.hoisted(() => vi.fn());
vi.mock("@/entities/branding", async (original) => {
  const actual = await original<typeof BrandingModule>();
  return { ...actual, brandingApi: { ...actual.brandingApi, save } };
});

const GRAY = {
  name: "Auth Sandbox",
  logo: null,
  color: null,
  colorDark: null,
  theme: "system",
} as const;

describe("форма оформления — как на сервере", () => {
  it("цвет — #rrggbb, сохраняется строчными; без своего цвета — null", () => {
    const valid = (color: string) =>
      brandingSchema.safeParse({ ...toForm(GRAY), hasColor: true, color }).success;
    expect(valid("#1F6FEB")).toBe(true);
    expect(valid("#fff")).toBe(false);
    expect(valid("red")).toBe(false);
    expect(fromForm({ ...toForm(GRAY), hasColor: true, color: "#1F6FEB" }).color).toBe("#1f6feb");
    expect(fromForm(toForm({ ...GRAY, color: "#1f6feb" }))).toEqual({ ...GRAY, color: "#1f6feb" });
    expect(fromForm({ ...toForm(GRAY), hasColor: false, color: "#1f6feb" }).color).toBeNull();
  });

  it("цвет для тёмной темы — только вместе со своим, проверяется, только если включён", () => {
    const form = { ...toForm(GRAY), hasColor: true, color: "#1f6feb" };
    const valid = (colorDark: string, hasColorDark = true) =>
      brandingSchema.safeParse({ ...form, hasColorDark, colorDark }).success;
    expect(valid("#7AA7FF")).toBe(true);
    expect(valid("#fff")).toBe(false);
    expect(valid("#fff", false)).toBe(true);
    expect(fromForm({ ...form, hasColorDark: true, colorDark: "#7AA7FF" }).colorDark).toBe(
      "#7aa7ff",
    );
    expect(fromForm({ ...form, hasColorDark: false, colorDark: "#7aa7ff" }).colorDark).toBeNull();
    // Свой цвет выключили — тёмный не уходит на сервер (иначе «тёмный без основного»).
    expect(
      fromForm({ ...form, hasColor: false, hasColorDark: true, colorDark: "#7aa7ff" }).colorDark,
    ).toBeNull();
    const saved = { ...GRAY, color: "#1f6feb", colorDark: "#7aa7ff" };
    expect(fromForm(toForm(saved))).toEqual(saved);
  });

  it("название обязательно, до 60 символов", () => {
    const valid = (name: string) => brandingSchema.safeParse({ ...toForm(GRAY), name }).success;
    expect(valid("  ")).toBe(false);
    expect(valid("x".repeat(60))).toBe(true);
    expect(valid("x".repeat(61))).toBe(false);
  });
});

describe("BrandingEditor", () => {
  beforeEach(() =>
    save.mockReset().mockImplementation((branding: unknown) => Promise.resolve(branding)),
  );

  it("тема «как в системе» — две миниатюры, «тёмная» — одна; сохраняет название, акценты и тему", async () => {
    const user = userEvent.setup();
    renderWithProviders(<BrandingEditor branding={GRAY} />);

    expect(screen.getByRole("figure", { name: /светлая тема/ })).toBeInTheDocument();
    expect(screen.getByRole("figure", { name: /тёмная тема/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Сохранить" })).toBeDisabled();

    await user.clear(screen.getByLabelText("Название проекта"));
    await user.type(screen.getByLabelText("Название проекта"), "Магазин");
    await user.click(screen.getByRole("radio", { name: "Свой цвет" }));
    await user.clear(screen.getByLabelText("Светлая тема, #rrggbb"));
    await user.type(screen.getByLabelText("Светлая тема, #rrggbb"), "#1F6FEB");
    expect(
      screen.getByRole("figure", { name: /тёмная тема/ }).style.getPropertyValue("--p-accent"),
    ).toBe("#1f6feb");
    await user.click(screen.getByRole("radio", { name: "Другой цвет" }));
    await user.clear(screen.getByLabelText("Цвет для тёмной темы, #rrggbb"));
    await user.type(screen.getByLabelText("Цвет для тёмной темы, #rrggbb"), "#7AA7FF");
    await user.click(screen.getByRole("radio", { name: "Тёмная" }));

    expect(screen.queryByRole("figure", { name: /светлая тема/ })).not.toBeInTheDocument();
    expect(
      screen.getByRole("figure", { name: /тёмная тема/ }).style.getPropertyValue("--p-accent"),
    ).toBe("#7aa7ff");
    expect(screen.getAllByText("Магазин").length).toBeGreaterThan(0);

    await user.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(save).toHaveBeenCalledWith({
      name: "Магазин",
      logo: null,
      color: "#1f6feb",
      colorDark: "#7aa7ff",
      theme: "dark",
    });
  });

  it("логотип: не картинка и больше 100 КБ — не берётся; PNG — в миниатюре, можно убрать", async () => {
    const user = userEvent.setup({ applyAccept: false });
    renderWithProviders(<BrandingEditor branding={GRAY} />);
    const input = screen.getByLabelText("Загрузить");

    await user.upload(input, new File(["<html>"], "page.html", { type: "text/html" }));
    expect(screen.getByText("Нужна картинка PNG, JPEG, WebP или SVG")).toBeInTheDocument();

    await user.upload(
      input,
      new File([new Uint8Array(100 * 1024 + 1)], "big.png", { type: "image/png" }),
    );
    expect(screen.getByText("Логотип — не больше 100 КБ")).toBeInTheDocument();

    await user.upload(input, new File([new Uint8Array(8)], "logo.png", { type: "image/png" }));
    expect((await screen.findAllByAltText("Логотип")).length).toBeGreaterThan(1);

    await user.click(screen.getByRole("button", { name: "Убрать" }));
    await user.click(screen.getByRole("button", { name: "Сохранить" }));
    expect(save).not.toHaveBeenCalled(); // убрали то, что только что загрузили — изменений нет
  });
});
