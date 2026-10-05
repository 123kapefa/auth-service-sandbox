import { QueryClient } from "@tanstack/react-query";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as AppModeModule from "@/entities/app-mode";
import { type AppMode, appModeQueries } from "@/entities/app-mode";
import { renderWithProviders } from "@/test/render";
import { AppModesCard } from "../ui/app-modes-card";

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock("sonner", () => ({ toast }));
const api = vi.hoisted(() => ({ switch: vi.fn() }));
vi.mock("@/entities/app-mode", async (original) => {
  const actual = await original<typeof AppModeModule>();
  return { ...actual, appModeApi: { ...actual.appModeApi, ...api } };
});

function renderCard(modes: AppMode[]) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(appModeQueries.list().queryKey, modes);
  return renderWithProviders(<AppModesCard />, queryClient);
}

describe("AppModesCard", () => {
  beforeEach(() => api.switch.mockReset().mockResolvedValue(null));

  it("режим переключается в приложении, где ролей больше одной", async () => {
    const user = userEvent.setup();
    renderCard([
      { app: "orders", name: "Заказы", modes: ["customer", "manager"], activeMode: "customer" },
      { app: "auth-api", name: "Сервис входа", modes: ["customer"], activeMode: "customer" },
    ]);

    expect(screen.getByText("Заказы")).toBeInTheDocument();
    expect(screen.queryByText("Сервис входа")).toBeNull();
    expect(screen.getByRole("button", { name: "customer" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );

    await user.click(screen.getByRole("button", { name: "manager" }));
    expect(api.switch).toHaveBeenCalledWith(
      { app: "orders", activeMode: "manager" },
      expect.anything(),
    );
  });

  it("у всех приложений по одной роли — карточки нет", () => {
    renderCard([{ app: "orders", name: "Заказы", modes: ["customer"], activeMode: "customer" }]);
    expect(screen.queryByText("Режим в приложениях")).toBeNull();
  });
});
