import { describe, expect, it } from "vitest";
import { accentCss, accentFor, applyBrandingAccent, textOn } from "../model";

describe("textOn — текст на цвете проекта", () => {
  it("на тёмном и насыщенном — светлый, на светлом — тёмный", () => {
    expect(textOn("#1f3a93")).toBe("#f7f7f7");
    expect(textOn("#000000")).toBe("#f7f7f7");
    expect(textOn("#c62828")).toBe("#f7f7f7");
    expect(textOn("#ffd54f")).toBe("#2e2e2e");
    expect(textOn("#ffffff")).toBe("#2e2e2e");
    expect(textOn("#4caf50")).toBe("#2e2e2e");
  });
});

describe("акцент проекта в кабинете", () => {
  it("светлый — для светлой темы, тёмный — для тёмной (выбранной и системной); без своего — тот же", () => {
    const css = accentCss({ color: "#1f6feb", colorDark: "#7aa7ff" });
    expect(css).toContain(":root{--color-accent:#1f6feb;--color-accent-foreground:#f7f7f7;");
    expect(css).toContain(':root[data-theme="dark"]{--color-accent:#7aa7ff;');
    expect(css).toContain(':root:not([data-theme="light"]){--color-accent:#7aa7ff;');
    expect(accentCss({ color: "#1f6feb", colorDark: null })).not.toContain("#7aa7ff");
    expect(accentFor({ color: "#1f6feb", colorDark: null }, "dark")).toBe("#1f6feb");
  });

  it("только акцент и фокус — линии (primary) и ошибки не трогает", () => {
    const css = accentCss({ color: "#c62828", colorDark: null });
    expect(css).not.toContain("--color-primary");
    expect(css).not.toContain("--color-danger");
  });

  it("одной таблицей стилей (CSSOM — CSP не пускает <style>); без цвета — серые токены темы", () => {
    const text = () =>
      document.adoptedStyleSheets.flatMap((sheet) =>
        [...sheet.cssRules].map((rule) => rule.cssText),
      );
    applyBrandingAccent({ color: "#1f6feb", colorDark: null });
    applyBrandingAccent({ color: "#aa3300", colorDark: null });
    expect(document.adoptedStyleSheets).toHaveLength(1);
    expect(text().join()).toContain("#aa3300");
    expect(text().join()).not.toContain("#1f6feb");

    applyBrandingAccent({ color: null, colorDark: null });
    expect(text()).toEqual([]);
  });
});
