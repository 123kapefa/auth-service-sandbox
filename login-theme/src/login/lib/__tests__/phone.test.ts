import { describe, expect, it } from "vitest";
import { formatLogin, nextLogin, toLogin } from "../phone";

describe("formatLogin — как видно в поле", () => {
  it("российский номер — +7 и маска по мере набора", () => {
    expect(formatLogin("7")).toBe("+7");
    expect(formatLogin("799")).toBe("+7 (99");
    expect(formatLogin("7999")).toBe("+7 (999)");
    expect(formatLogin("79990000001")).toBe("+7 (999) 000-00-01");
  });

  it("с 8 и 10 цифр без кода — тоже +7", () => {
    expect(formatLogin("89990000001")).toBe("+7 (999) 000-00-01");
    expect(formatLogin("9990000001")).toBe("+7 (999) 000-00-01");
  });

  it("уже оформленный номер не меняется; лишние цифры не набираются", () => {
    expect(formatLogin("+7 (999) 000-00-01")).toBe("+7 (999) 000-00-01");
    expect(formatLogin("+7 (999) 000-00-0199")).toBe("+7 (999) 000-00-01");
  });

  it("другие страны — «+» и цифры; «+8…» набран явно — не трогаем", () => {
    expect(formatLogin("+375291234567")).toBe("+375291234567");
    expect(formatLogin("+8")).toBe("+8");
  });

  it("почта и пустое — как есть, одинокий «+» убирается", () => {
    expect(formatLogin("user@mail.ru")).toBe("user@mail.ru");
    expect(formatLogin("")).toBe("");
    expect(formatLogin("+")).toBe("");
  });
});

describe("toLogin — что уходит на сервер", () => {
  it("номер в любом виде — цифры с кодом страны", () => {
    expect(toLogin("+7 (999) 000-00-01")).toBe("79990000001");
    expect(toLogin("8 999 000 00 01")).toBe("79990000001");
    expect(toLogin("9990000001")).toBe("79990000001");
    expect(toLogin("+375291234567")).toBe("375291234567");
  });

  it("почта и прочее — как есть", () => {
    expect(toLogin("user@mail.ru")).toBe("user@mail.ru");
    expect(toLogin("ivan")).toBe("ivan");
  });
});

describe("nextLogin — правка поля", () => {
  it("Backspace по знаку маски стирает и цифру", () => {
    expect(nextLogin("+7 (999)", "+7 (999")).toBe("+7 (99");
    expect(nextLogin("+7 (999) 000-0", "+7 (999) 000-")).toBe("+7 (999) 000");
  });

  it("обычный набор и стирание цифры — как обычно", () => {
    expect(nextLogin("+7 (99", "+7 (999")).toBe("+7 (999)");
    expect(nextLogin("+7 (999) 0", "+7 (999) ")).toBe("+7 (999)");
    expect(nextLogin("+7", "")).toBe("");
  });

  it("почта — без маски", () => {
    expect(nextLogin("user@mail.r", "user@mail.ru")).toBe("user@mail.ru");
  });
});
