import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as SettingsModule from "@/entities/sign-in-settings";
import { type ProjectSignIn } from "@/entities/sign-in-settings";
import { renderWithProviders } from "@/test/render";
import { daysAhead, keepsRegistration, loginFieldAfter } from "../model/rules";
import { ProjectMethodsCard } from "../ui/project-methods-card";

const saveMethods = vi.hoisted(() => vi.fn());
const affected = vi.hoisted(() => vi.fn());
vi.mock("@/entities/sign-in-settings", async (original) => {
  const actual = await original<typeof SettingsModule>();
  return {
    ...actual,
    signInSettingsApi: { ...actual.signInSettingsApi, saveMethods, affected },
    signInSettingsQueries: {
      ...actual.signInSettingsQueries,
      affected: (method: SettingsModule.ProjectMethod) => ({
        ...actual.signInSettingsQueries.affected(method),
        queryFn: () => affected(method),
      }),
    },
  };
});

const ALL_ON: ProjectSignIn = {
  methods: {
    phone: { status: "on" },
    email: { status: "on" },
    password: { status: "on" },
    passkey: { status: "on" },
    yandex: { status: "on", configured: true },
    esia: { status: "off", configured: false },
  },
  twoFactor: "optional",
};

describe("правила способов проекта", () => {
  it("последний способ регистрации не выключить; поле входа после выключения", () => {
    const onlyPhone: ProjectSignIn = {
      ...ALL_ON,
      methods: {
        ...ALL_ON.methods,
        email: { status: "off" },
        yandex: { status: "transition", until: "2026-11-01" },
      },
    };
    expect(keepsRegistration(ALL_ON, "phone")).toBe(true);
    expect(keepsRegistration(onlyPhone, "phone")).toBe(false);
    expect(keepsRegistration(onlyPhone, "password")).toBe(true);
    expect(loginFieldAfter(ALL_ON, "phone")).toBe("Почта");
    expect(loginFieldAfter(ALL_ON, "email")).toBe("Номер телефона");
    expect(loginFieldAfter(onlyPhone, "phone")).toBeNull();
    expect(loginFieldAfter(ALL_ON, "password")).toBeNull();
    expect(daysAhead(30, new Date(2026, 9, 7))).toBe("2026-11-06");
  });
});

describe("ProjectMethodsCard", () => {
  beforeEach(() => {
    saveMethods
      .mockReset()
      .mockImplementation((settings: ProjectSignIn) => Promise.resolve(settings));
    affected.mockReset().mockResolvedValue({ method: "phone", users: 8410, only: 1240 });
  });

  it("выключить телефон с переходным периодом: сколько людей только так, дата по умолчанию — через 30 дней", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProjectMethodsCard settings={ALL_ON} />);

    await user.click(screen.getByRole("button", { name: "Выключить: Вход по телефону" }));
    const dialog = await screen.findByRole("dialog", { name: "Выключить вход по телефону?" });
    expect(within(dialog).getByText("Способы входа в проекте")).toBeInTheDocument();
    expect(await within(dialog).findByText(/1\s240 человек/)).toBeInTheDocument();
    expect(within(dialog).getByText(/Поле входа станет «Почта»/)).toBeInTheDocument();
    expect(within(dialog).getByLabelText("С переходным периодом до")).toBeChecked();
    expect(within(dialog).getByLabelText("Конец переходного периода")).toHaveValue(daysAhead(30));

    await user.click(within(dialog).getByRole("button", { name: "Выключить с переходом" }));
    expect(saveMethods).toHaveBeenCalledWith({
      ...ALL_ON,
      methods: { ...ALL_ON.methods, phone: { status: "transition", until: daysAhead(30) } },
    });
  });

  it("«Сразу» — выключено; ключ доступа — без переходного периода", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProjectMethodsCard settings={ALL_ON} />);

    await user.click(screen.getByRole("button", { name: "Выключить: Вход по почте" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("radio", { name: /Сразу/ }));
    await user.click(within(dialog).getByRole("button", { name: "Выключить" }));
    expect(saveMethods).toHaveBeenLastCalledWith({
      ...ALL_ON,
      methods: { ...ALL_ON.methods, email: { status: "off" } },
    });

    await user.click(screen.getByRole("button", { name: "Выключить: Ключ доступа" }));
    const passkey = await screen.findByRole("dialog", { name: "Выключить ключи доступа?" });
    expect(within(passkey).queryByRole("radio")).not.toBeInTheDocument();
  });

  it("Госуслуги без ключей не включить; 2FA — обязательной после подтверждения", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ProjectMethodsCard settings={ALL_ON} />);

    expect(screen.getByRole("button", { name: "Включить: Госуслуги" })).toBeDisabled();
    expect(screen.getByText(/нужны ключи проекта/)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Сделать обязательной" }));
    const dialog = await screen.findByRole("dialog", { name: "Сделать 2FA обязательной?" });
    await user.click(within(dialog).getByRole("button", { name: "Сделать обязательной" }));
    expect(saveMethods).toHaveBeenCalledWith({ ...ALL_ON, twoFactor: "required" });
  });
});
