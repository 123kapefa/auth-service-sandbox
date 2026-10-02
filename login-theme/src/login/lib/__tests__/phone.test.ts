import { describe, expect, it } from "vitest";
import { toLogin, withPlus } from "../phone";

describe("withPlus", () => {
  it("цифры — с «+», почта и пустое — как есть, одинокий «+» убирается", () => {
    expect(withPlus("7999")).toBe("+7999");
    expect(withPlus("+7999")).toBe("+7999");
    expect(withPlus("user@mail.ru")).toBe("user@mail.ru");
    expect(withPlus("+")).toBe("");
    expect(withPlus("")).toBe("");
  });
});

describe("toLogin", () => {
  it("номер в любом виде — цифрами; почта — как есть", () => {
    expect(toLogin("+7 (999) 000-00-01")).toBe("79990000001");
    expect(toLogin("8 999 000 00 01")).toBe("89990000001");
    expect(toLogin("user@mail.ru")).toBe("user@mail.ru");
    expect(toLogin("ivan")).toBe("ivan");
  });
});
