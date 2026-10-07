import { QueryClient } from "@tanstack/react-query";
import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { type SignInMethods } from "@/entities/account-security";
import { userQueries } from "@/entities/user";
import { renderWithProviders } from "@/test/render";
import { SignInMethodsSection } from "../ui/sign-in-methods-section";

const METHODS: SignInMethods = {
  methods: [
    { method: "password", available: true, enabled: false, disableBlocker: null },
    { method: "phone-code", available: true, enabled: true, disableBlocker: null },
    { method: "email-code", available: false, enabled: true, disableBlocker: "not_available" },
  ],
  hasPasskey: true,
  hasAuthenticator: false,
  hasRecoveryCodes: false,
  linkedProviders: ["yandex"],
  history: [{ at: "2026-10-05T15:40:00Z", method: "password", enabled: false, byAdmin: false }],
  projectDisabled: [],
};

function render(props: { canReset: boolean; self: boolean }, methods = METHODS) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(userQueries.signInMethods("u1").queryKey, methods);
  return renderWithProviders(<SignInMethodsSection userId="u1" {...props} />, queryClient);
}

describe("SignInMethodsSection", () => {
  it("что человек выключил сам и когда; ключи, 2FA, внешний вход; кнопка сброса", () => {
    render({ canReset: true, self: false });

    expect(screen.getByText("выключен")).toBeInTheDocument();
    expect(screen.getByText(/человеком, /)).toBeInTheDocument();
    expect(screen.getByText("нет почты")).toBeInTheDocument();
    expect(screen.getByText("не подключена")).toBeInTheDocument();
    expect(screen.getByText("Яндекс ID")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Вернуть способы по умолчанию" })).toBeEnabled();
  });

  it("себе и когда всё включено — сбросить нельзя; без права — панели нет", () => {
    const { unmount } = render({ canReset: true, self: true });
    expect(screen.getByRole("button", { name: "Вернуть способы по умолчанию" })).toBeDisabled();
    unmount();

    const allOn = {
      ...METHODS,
      methods: METHODS.methods.map((item) => ({ ...item, enabled: true })),
    };
    const second = render({ canReset: true, self: false }, allOn);
    expect(screen.getByText("Всё и так включено.")).toBeInTheDocument();
    second.unmount();

    render({ canReset: false, self: false });
    expect(screen.queryByRole("button", { name: /Вернуть/ })).not.toBeInTheDocument();
  });
});
