import { afterEach, describe, expect, it, vi } from "vitest";
import {
  accentCss,
  applyBranding,
  brandingUrl,
  getBranding,
  loadBranding,
  textOn,
} from "../branding";

describe("оформление проекта на страницах входа", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("адрес плагина — рядом со страницей входа (и за прокси с префиксом)", () => {
    expect(brandingUrl("/realms/shop/login-actions/authenticate", "shop")).toBe(
      "/realms/shop/platform-branding",
    );
    expect(brandingUrl("/auth/realms/shop/protocol/openid-connect/auth", "shop")).toBe(
      "/auth/realms/shop/platform-branding",
    );
    expect(brandingUrl("/", "shop")).toBe("/realms/shop/platform-branding");
  });

  it("плагину — клиент входа и вкладка: по ним он находит приложение", () => {
    expect(
      brandingUrl("/realms/shop/login-actions/authenticate", "shop", {
        clientId: "orders-web",
        loginAction:
          "/realms/shop/login-actions/authenticate?session_code=x&execution=y&client_id=orders-web&tab_id=Ab_c-1",
      }),
    ).toBe("/realms/shop/platform-branding?client_id=orders-web&tab_id=Ab_c-1");
    expect(brandingUrl("/realms/shop/x", "shop", { clientId: "auth-bff" })).toBe(
      "/realms/shop/platform-branding?client_id=auth-bff",
    );
  });

  it("тема закрепляется на <html>, акцент — в токены accent и ring (свой для тёмной темы), линии не трогает", () => {
    const root = document.createElement("html");
    const accent = () =>
      document.adoptedStyleSheets
        .flatMap((sheet) => [...sheet.cssRules].map((rule) => rule.cssText))
        .join("\n")
        .replaceAll(": ", ":")
        .replaceAll("; ", ";")
        .replaceAll(" {", "{")
        .replaceAll("{ ", "{");

    applyBranding(
      { name: "Магазин", logo: null, color: "#1f3a93", colorDark: "#7aa7ff", theme: "dark" },
      root,
    );
    expect(root.dataset.theme).toBe("dark");
    expect(accent()).toContain(":root{--color-accent:#1f3a93;--color-accent-foreground:#f7f7f7;");
    expect(accent()).toContain(':root[data-theme="dark"]{--color-accent:#7aa7ff;');
    expect(accent()).toContain("--color-ring:#7aa7ff;");
    expect(accent()).not.toContain("--color-primary");
    document.adoptedStyleSheets = [];

    applyBranding(
      { name: "Магазин", logo: null, color: null, colorDark: null, theme: "system" },
      root,
    );
    expect(root.dataset.theme).toBeUndefined();
    expect(document.adoptedStyleSheets).toEqual([]);
  });

  it("без своего тёмного — тот же цвет и в тёмной теме", () => {
    expect(accentCss("#aa3300", "#aa3300")).not.toContain("#cdcdcd");
  });

  it("текст на светлом цвете — тёмный, на тёмном — светлый", () => {
    expect(textOn("#ffd54f")).toBe("#2e2e2e");
    expect(textOn("#1f3a93")).toBe("#f7f7f7");
  });

  it("чужое в ответе отбрасывается: логотип не картинкой, цвет не #rrggbb, неизвестная тема", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({
            name: "Магазин",
            logo: "javascript:alert(1)",
            color: "red",
            colorDark: "#7aa7ff",
            theme: "neon",
          }),
        ),
      ),
    );

    expect(await loadBranding("shop")).toEqual({
      name: "Магазин",
      logo: null,
      color: null,
      // Тёмный без основного не берётся: страница целиком серая.
      colorDark: null,
      theme: "system",
    });
    expect(getBranding()?.name).toBe("Магазин");
  });

  it("плагин недоступен — страница без оформления, без ошибки", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network")));
    expect(await loadBranding("shop")).toBeNull();

    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("", { status: 404 })));
    expect(await loadBranding("shop")).toBeNull();
  });
});
