import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { adminSessionApi } from "@/entities/admin-session";
import { renderWithProviders } from "@/test/render";
import { NoAccessPage } from "../ui/no-access-page";

afterEach(() => {
  vi.restoreAllMocks();
});

describe("NoAccessPage", () => {
  it("«Войти другим пользователем» — выход и вход с возвратом в админку", async () => {
    const logout = vi.spyOn(adminSessionApi, "logout").mockResolvedValue();
    const assign = vi.fn();
    vi.spyOn(window, "location", "get").mockReturnValue({ ...window.location, assign });
    renderWithProviders(<NoAccessPage activeMode="customer" />);

    expect(screen.getByText(/В режиме «customer»/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "В личный кабинет" })).toHaveAttribute(
      "href",
      "/account/",
    );

    await userEvent.click(screen.getByRole("button", { name: "Войти другим пользователем" }));
    expect(logout).toHaveBeenCalled();
    await waitFor(() => expect(assign).toHaveBeenCalledWith("/auth/login?return_to=%2Fadmin%2F"));
  });
});
