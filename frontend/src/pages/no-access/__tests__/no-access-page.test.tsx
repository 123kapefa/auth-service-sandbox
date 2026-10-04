import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { adminSessionApi } from "@/entities/admin-session";
import { renderWithProviders } from "@/test/render";
import { NoAccessPage } from "../ui/no-access-page";

afterEach(() => {
  vi.restoreAllMocks();
});

function mockLocation() {
  const location = { ...window.location, assign: vi.fn(), reload: vi.fn() };
  vi.spyOn(window, "location", "get").mockReturnValue(location);
  return location;
}

describe("NoAccessPage", () => {
  it("сотрудник в режиме покупателя переключается в рабочий режим", async () => {
    vi.spyOn(adminSessionApi, "modes").mockResolvedValue(["customer", "admin"]);
    const switchMode = vi.spyOn(adminSessionApi, "switchMode").mockResolvedValue();
    const location = mockLocation();
    renderWithProviders(<NoAccessPage activeMode="customer" />);

    await userEvent.click(await screen.findByRole("button", { name: "Режим «admin»" }));
    expect(switchMode).toHaveBeenCalledWith("admin", expect.anything());
    await waitFor(() => expect(location.reload).toHaveBeenCalled());
    expect(screen.queryByRole("button", { name: "Режим «customer»" })).not.toBeInTheDocument();
  });

  it("«Войти другим пользователем» — выход и вход с возвратом в админку", async () => {
    vi.spyOn(adminSessionApi, "modes").mockResolvedValue(["customer"]);
    const logout = vi.spyOn(adminSessionApi, "logout").mockResolvedValue();
    const location = mockLocation();
    renderWithProviders(<NoAccessPage activeMode="customer" />);

    expect(screen.getByRole("link", { name: "В личный кабинет" })).toHaveAttribute(
      "href",
      "/account/",
    );
    await userEvent.click(screen.getByRole("button", { name: "Войти другим пользователем" }));
    expect(logout).toHaveBeenCalled();
    await waitFor(() =>
      expect(location.assign).toHaveBeenCalledWith("/auth/login?return_to=%2Fadmin%2F"),
    );
  });
});
