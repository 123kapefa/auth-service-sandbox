import { describe, expect, it } from "vitest";
import { deviceName, loginErrorLabel } from "../model";

describe("loginErrorLabel", () => {
  it("известная причина — по-русски, неизвестная — с кодом", () => {
    expect(loginErrorLabel("invalid_user_credentials")).toBe("неверный пароль или код");
    expect(loginErrorLabel("something_new")).toBe("ошибка (something_new)");
    expect(loginErrorLabel(null)).toBe("ошибка");
  });
});

describe("deviceName", () => {
  it("не узнали — «Неизвестное устройство»", () => {
    expect(deviceName("Chrome · macOS")).toBe("Chrome · macOS");
    expect(deviceName(null)).toBe("Неизвестное устройство");
  });
});
