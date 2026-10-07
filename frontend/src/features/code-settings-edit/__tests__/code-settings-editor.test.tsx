import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as SettingsModule from "@/entities/sign-in-settings";
import { type CodeSettings } from "@/entities/sign-in-settings";
import { renderWithProviders } from "@/test/render";
import {
  codeSettingsSchema,
  fromForm,
  parseCountries,
  parseRubles,
  toForm,
} from "../model/schemas";
import { CodeSettingsEditor } from "../ui/code-settings-editor";

const saveCodes = vi.hoisted(() => vi.fn());
vi.mock("@/entities/sign-in-settings", async (original) => {
  const actual = await original<typeof SettingsModule>();
  return { ...actual, signInSettingsApi: { ...actual.signInSettingsApi, saveCodes } };
});

const SETTINGS: CodeSettings = {
  phoneChannels: ["call", "sms"],
  countries: ["7"],
  dailyBudget: 300_000,
  targetPerHour: 5,
  targetPerDay: 10,
  ipPerHour: 10,
  rangePerHour: 20,
  captchaAfter: 2,
  rangeDigits: 7,
  smsCost: 500,
  callCost: 80,
  budgetAlertPercent: 80,
  zone: "Europe/Moscow",
};

describe("форма кодов — как на сервере", () => {
  it("страны: «+7, +375» → коды без плюса; не код — null", () => {
    expect(parseCountries("+7, +375 7")).toEqual(["7", "375"]);
    expect(parseCountries("")).toEqual([]);
    expect(parseCountries("+0")).toBeNull();
    expect(parseCountries("RU")).toBeNull();
  });

  it("бюджет: рубли с пробелами и копейками → копейки", () => {
    expect(parseRubles("3 000")).toBe(300_000);
    expect(parseRubles("1500,5")).toBe(150_050);
    expect(parseRubles("-1")).toBeNull();
    expect(fromForm(toForm(SETTINGS))).toEqual({
      phoneChannels: ["call", "sms"],
      countries: ["7"],
      dailyBudget: 300_000,
      targetPerHour: 5,
      targetPerDay: 10,
      ipPerHour: 10,
      rangePerHour: 20,
      captchaAfter: 2,
    });
  });

  it("без звонков и SMS, в сутки меньше, чем в час, бюджет больше 10 млн — не проходит", () => {
    const form = toForm(SETTINGS);
    const valid = (changes: Partial<typeof form>) =>
      codeSettingsSchema.safeParse({ ...form, ...changes }).success;
    expect(valid({})).toBe(true);
    expect(valid({ call: false, sms: false })).toBe(false);
    expect(valid({ call: false })).toBe(true);
    expect(valid({ targetPerDay: "4" })).toBe(false);
    expect(valid({ budget: "10000001" })).toBe(false);
    expect(valid({ budget: "0" })).toBe(true);
    expect(valid({ countries: "" })).toBe(false);
    expect(valid({ targetPerHour: "101" })).toBe(false);
    expect(valid({ captchaAfter: "0" })).toBe(true);
  });
});

describe("CodeSettingsEditor", () => {
  beforeEach(() =>
    saveCodes
      .mockReset()
      .mockImplementation((changes: object) => Promise.resolve({ ...SETTINGS, ...changes })),
  );

  it("выключили звонки, поменяли страны и бюджет — уходит всё целиком; порядок показывает выключенное", async () => {
    const user = userEvent.setup();
    renderWithProviders(<CodeSettingsEditor settings={SETTINGS} />);
    expect(screen.getByRole("button", { name: "Сохранить" })).toBeDisabled();

    await user.click(screen.getByRole("checkbox", { name: "Звонки" }));
    expect(screen.getByText("Звонок").closest("li")).toHaveTextContent("(выключено)");
    await user.clear(screen.getByLabelText("Страны"));
    await user.type(screen.getByLabelText("Страны"), "+7, +375");
    await user.clear(screen.getByLabelText("Дневной бюджет, ₽"));
    await user.type(screen.getByLabelText("Дневной бюджет, ₽"), "5 000");
    await user.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(saveCodes).toHaveBeenCalledWith({
      phoneChannels: ["sms"],
      countries: ["7", "375"],
      dailyBudget: 500_000,
      targetPerHour: 5,
      targetPerDay: 10,
      ipPerHour: 10,
      rangePerHour: 20,
      captchaAfter: 2,
    });
  });

  it("ошибки — у полей, на сервер не уходит", async () => {
    const user = userEvent.setup();
    renderWithProviders(<CodeSettingsEditor settings={SETTINGS} />);

    await user.click(screen.getByRole("checkbox", { name: "Звонки" }));
    await user.click(screen.getByRole("checkbox", { name: "SMS" }));
    await user.clear(screen.getByLabelText("На номер в сутки"));
    await user.type(screen.getByLabelText("На номер в сутки"), "3");
    await user.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(await screen.findByText("Оставьте звонки или SMS")).toBeInTheDocument();
    expect(screen.getByText("В сутки — не меньше, чем в час")).toBeInTheDocument();
    expect(saveCodes).not.toHaveBeenCalled();
  });
});
