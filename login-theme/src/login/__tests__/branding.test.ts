import { afterEach, describe, expect, it, vi } from "vitest";
import { applyBranding, brandingUrl, getBranding, loadBranding, textOn } from "../branding";

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

  it("тема закрепляется на <html>, цвет — в токены primary и ring, текст на цвете — по яркости", () => {
    const root = document.createElement("html");

    applyBranding({ name: "Магазин", logo: null, color: "#1f3a93", theme: "dark" }, root);
    expect(root.dataset.theme).toBe("dark");
    expect(root.style.getPropertyValue("--color-primary")).toBe("#1f3a93");
    expect(root.style.getPropertyValue("--color-ring")).toBe("#1f3a93");
    expect(root.style.getPropertyValue("--color-primary-foreground")).toBe("#f7f7f7");

    applyBranding({ name: "Магазин", logo: null, color: null, theme: "system" }, root);
    expect(root.dataset.theme).toBeUndefined();
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
            theme: "neon",
          }),
        ),
      ),
    );

    expect(await loadBranding("shop")).toEqual({
      name: "Магазин",
      logo: null,
      color: null,
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
