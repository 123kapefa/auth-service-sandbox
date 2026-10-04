import { QueryClient } from "@tanstack/react-query";
import { screen, waitFor } from "@testing-library/react";
import { Suspense } from "react";
import { useLocation } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { type AccountProfile, accountProfileQueries } from "@/entities/account-profile";
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

function renderPage(data: AccountProfile, route = "/") {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(accountProfileQueries.profile().queryKey, data);
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

  it("телефон, почта со статусом, ссылки на смену пароля и почты — через вход с действием", () => {
    renderPage(profile);

    expect(screen.getByText("+79990000001")).toBeInTheDocument();
    expect(screen.getByText("customer@sandbox.test")).toBeInTheDocument();
    expect(screen.getByText("подтверждена")).toBeInTheDocument();

    const password = linkUrl("Сменить пароль");
    expect(password.pathname).toBe("/auth/login");
    expect(password.searchParams.get("action")).toBe("update-password");
    expect(password.searchParams.get("return_to")).toBe("/account/?action=update-password");
    expect(linkUrl("Сменить почту").searchParams.get("action")).toBe("update-email");
    expect(linkUrl("Настроить").pathname).toBe("/auth/account/security");
  });

  it("без почты — «Добавить почту»", () => {
    renderPage({ ...profile, email: null, emailVerified: false });
    expect(screen.getByText("не указана")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Добавить почту" })).toBeInTheDocument();
  });

  it("почта не подтверждена — так и сказано", () => {
    renderPage({ ...profile, emailVerified: false });
    expect(screen.getByText("не подтверждена")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Сменить почту" })).toBeInTheDocument();
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
