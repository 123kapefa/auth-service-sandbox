import { QueryClient } from "@tanstack/react-query";
import { screen, within } from "@testing-library/react";
import { Suspense } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { type AccountSecurity, accountSecurityQueries } from "@/entities/account-security";
import { renderWithProviders } from "@/test/render";
import { AccountSecurityPage } from "../ui/account-security-page";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const nothing: AccountSecurity = {
  hasPassword: true,
  authenticators: [],
  passkeys: [],
  recoveryCodes: null,
};

function renderPage(data: AccountSecurity) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(accountSecurityQueries.security().queryKey, data);
  return renderWithProviders(
    <Suspense>
      <AccountSecurityPage />
    </Suspense>,
    queryClient,
  );
}

const section = (name: string) => within(screen.getByRole("region", { name }));
const href = (link: HTMLElement) => new URL(link.getAttribute("href") ?? "", "http://x");

describe("AccountSecurityPage", () => {
  // Ссылки возвращают на текущую страницу кабинета.
  beforeEach(() => window.history.pushState({}, "", "/account/security"));

  it("ничего не подключено — подключить; коды — только после приложения; удалять нечего", () => {
    renderPage(nothing);

    expect(screen.queryByRole("link", { name: "Удалить" })).toBeNull();
    const totp = href(
      section("Вход с кодом из приложения (2FA)").getByRole("link", { name: "Подключить" }),
    );
    expect(totp.pathname).toBe("/auth/login");
    expect(totp.searchParams.get("action")).toBe("configure-totp");
    expect(totp.searchParams.get("credential")).toBeNull();
    expect(section("Коды восстановления").queryByRole("link", { name: "Создать" })).toBeNull();
    expect(
      section("Коды восстановления").getByText("Сначала подключите приложение-аутентификатор"),
    ).toBeInTheDocument();
    expect(
      href(section("Пароль").getByRole("link", { name: "Сменить" })).searchParams.get("action"),
    ).toBe("update-password");
    expect(
      href(section("Ключи доступа").getByRole("link", { name: "Добавить" })).searchParams.get(
        "action",
      ),
    ).toBe("register-passkey");
  });

  it("подключённое — подпись и «Удалить» с Id; без подписи — общее название", () => {
    renderPage({
      ...nothing,
      authenticators: [{ id: "otp-1", label: "Телефон", createdAt: "2026-10-01T10:00:00Z" }],
      passkeys: [{ id: "key-1", label: null, createdAt: "2026-10-02T10:00:00Z" }],
      recoveryCodes: { id: "codes-1", createdAt: "2026-10-01T10:00:00Z", remaining: 9, total: 12 },
    });

    const totp = section("Вход с кодом из приложения (2FA)");
    expect(totp.getByText("Телефон")).toBeInTheDocument();
    const remove = href(totp.getByRole("link", { name: "Удалить" }));
    expect(remove.searchParams.get("action")).toBe("delete-credential");
    expect(remove.searchParams.get("credential")).toBe("otp-1");
    expect(remove.searchParams.get("return_to")).toBe("/account/security?action=delete-credential");
    expect(totp.getByRole("link", { name: "Подключить ещё" })).toBeInTheDocument();

    expect(section("Ключи доступа").getByText("Ключ доступа")).toBeInTheDocument();
    expect(
      href(section("Ключи доступа").getByRole("link", { name: "Удалить" })).searchParams.get(
        "credential",
      ),
    ).toBe("key-1");

    const codes = section("Коды восстановления");
    expect(codes.getByText(/Осталось 9 из 12/)).toBeInTheDocument();
    expect(href(codes.getByRole("link", { name: "Удалить" })).searchParams.get("credential")).toBe(
      "codes-1",
    );
    expect(codes.getByRole("link", { name: "Создать новые" })).toBeInTheDocument();
    expect(totp.getByText(/удалите и их/)).toBeInTheDocument();
  });

  it("коды остались без приложения — предупреждение и «Удалить», создать новые нельзя", () => {
    renderPage({
      ...nothing,
      recoveryCodes: { id: "codes-1", createdAt: "2026-10-01T10:00:00Z", remaining: 3, total: 12 },
    });

    const codes = section("Коды восстановления");
    expect(codes.getByRole("alert")).toHaveTextContent("при входе их спрашивают");
    expect(href(codes.getByRole("link", { name: "Удалить" })).searchParams.get("credential")).toBe(
      "codes-1",
    );
    expect(codes.queryByRole("link", { name: "Создать новые" })).toBeNull();
  });

  it("пароля нет — «Задать»", () => {
    renderPage({ ...nothing, hasPassword: false });
    expect(section("Пароль").getByRole("link", { name: "Задать" })).toBeInTheDocument();
  });
});
