import { QueryClient } from "@tanstack/react-query";
import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { serviceKeyQueries } from "@/entities/service-key";
import { renderWithProviders } from "@/test/render";
import { ServicesPage } from "../ui/services-page";

describe("ServicesPage", () => {
  it("сервисы с правами по-русски, без секретов; у каждого — права, перевыпуск, отзыв", () => {
    const queryClient = new QueryClient();
    queryClient.setQueryData(serviceKeyQueries.list().queryKey, [
      {
        code: "orders",
        clientId: "service-orders",
        name: "Заказы",
        issuedAt: "2026-10-03T12:00:00Z",
        rotatedAt: null,
        permissions: ["service.users.read"],
      },
      {
        code: "notifications",
        clientId: "service-notifications",
        name: "Уведомления",
        issuedAt: "2026-10-03T12:00:00Z",
        rotatedAt: "2026-10-03T13:00:00Z",
        permissions: [],
      },
    ]);

    renderWithProviders(<ServicesPage />, queryClient);

    const orders = screen.getByText("Заказы").closest("li") as HTMLElement;
    expect(within(orders).getByText("service-orders")).toBeInTheDocument();
    expect(within(orders).getByText("Читать пользователя")).toBeInTheDocument();
    for (const action of ["Права", "Перевыпустить", "Отозвать"]) {
      expect(within(orders).getByRole("button", { name: action })).toBeInTheDocument();
    }

    const notifications = screen.getByText("Уведомления").closest("li") as HTMLElement;
    expect(within(notifications).getByText("в сервисе входа — без прав")).toBeInTheDocument();
    expect(within(notifications).getByText(/перевыпущен/)).toBeInTheDocument();
  });
});
