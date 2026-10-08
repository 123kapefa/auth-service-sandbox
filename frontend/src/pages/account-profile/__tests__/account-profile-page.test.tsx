import { QueryClient } from "@tanstack/react-query";
import { screen, waitFor } from "@testing-library/react";
import { Suspense } from "react";
import { useLocation } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { type AccountProfile, accountProfileQueries } from "@/entities/account-profile";
import { accountSecurityQueries } from "@/entities/account-security";
import { appModeQueries } from "@/entities/app-mode";
import { renderWithProviders } from "@/test/render";
import { AccountProfilePage } from "../ui/account-profile-page";

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock("sonner", () => ({ toast }));

const profile: AccountProfile = {
  id: "a2b3805f-2d09-4c35-ab2e-adc8ae75b01c",
  phone: "+79990000001",
  email: "customer@sandbox.test",
  emailVerified: true,
  createdAt: "2026-10-01T10:00:00Z",
};

function Location() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname + location.search}</output>;
}

/** Адрес ссылки по её тексту — разобранный, чтобы проверять параметры. */
function linkUrl(name: string): URL {
  return new URL(screen.getByRole("link", { name }).getAttribute("href") ?? "", "http://x");
}

function renderPage(data: AccountProfile, route = "/", projectDisabled: string[] = []) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(accountProfileQueries.profile().queryKey, data);
  queryClient.setQueryData(accountSecurityQueries.signInMethods().queryKey, {
    methods: [],
    hasPasskey: false,
    hasAuthenticator: false,
    hasRecoveryCodes: false,
    linkedProviders: [],
    unlinkBlockers: {},
    history: [],
    projectDisabled,
  });
  queryClient.setQueryData(appModeQueries.list().queryKey, []);
  return renderWithProviders(
    <Suspense>
      <AccountProfilePage />
      <Location />
    </Suspense>,
    queryClient,
    route,
  );
}

describe("AccountProfilePage", () => {
  beforeEach(() => {
    toast.success.mockReset();
    toast.error.mockReset();
  });

  it("телефон, почта со статусом, ссылка на смену почты — через вход с действием", () => {
    renderPage(profile);

    expect(screen.getByText("+7 999 000-00-01")).toBeInTheDocument();
    expect(screen.getByText("с 01.10.2026")).toBeInTheDocument();
    expect(screen.getByText("customer@sandbox.test")).toBeInTheDocument();
    expect(screen.getByText("подтверждена")).toBeInTheDocument();

    const email = linkUrl("Сменить");
    expect(email.pathname).toBe("/auth/login");
    expect(email.searchParams.get("action")).toBe("update-email");
    expect(email.searchParams.get("return_to")).toBe("/account/?action=update-email");
  });

  it("без почты — «Добавить почту»", () => {
    renderPage({ ...profile, email: null, emailVerified: false });
    expect(screen.getByText("не указана")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Добавить почту" })).toBeInTheDocument();
  });

  it("почта не подтверждена — так и сказано", () => {
    renderPage({ ...profile, emailVerified: false });
    expect(screen.getByText("не подтверждена")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Сменить" })).toBeInTheDocument();
  });

  it("«Сменить номер» — через вход с действием update-phone; дата — смены номера", () => {
    renderPage({ ...profile, phoneChangedAt: "2026-10-06T12:00:00Z" });

    const phone = linkUrl("Сменить номер");
    expect(phone.pathname).toBe("/auth/login");
    expect(phone.searchParams.get("action")).toBe("update-phone");
    expect(phone.searchParams.get("return_to")).toBe("/account/?action=update-phone");
    expect(screen.getByText("с 06.10.2026")).toBeInTheDocument();
  });

  it("вход по телефону выключен в проекте — смены номера нет", () => {
    renderPage(profile, "/", ["phone"]);
    expect(screen.queryByRole("link", { name: "Сменить номер" })).toBeNull();
  });

  it("возврат после смены пароля — уведомление, профиль перечитан, адрес очищен", async () => {
    const invalidate = vi.spyOn(QueryClient.prototype, "invalidateQueries");
    renderPage(profile, "/?action=update-password&action_status=success");

    await waitFor(() =>
      expect(toast.success).toHaveBeenCalledWith("Пароль изменён", { id: "account-action" }),
    );
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent(/^\/$/));
    expect(invalidate).toHaveBeenCalledWith(accountProfileQueries.profile());
    invalidate.mockRestore();
  });

  it("отмена — без уведомлений, адрес очищен", async () => {
    renderPage(profile, "/?action=update-email&action_status=cancelled");

    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent(/^\/$/));
    expect(toast.success).not.toHaveBeenCalled();
    expect(toast.error).not.toHaveBeenCalled();
  });
});
