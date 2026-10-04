import { describe, expect, it } from "vitest";
import { textOn } from "../model";

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
