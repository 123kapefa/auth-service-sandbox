import { QueryClient } from "@tanstack/react-query";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as ServiceKeyModule from "@/entities/service-key";
import { type ServiceKey, serviceKeyQueries } from "@/entities/service-key";
import { renderWithProviders } from "@/test/render";
import { ServiceKeyActions } from "../ui/service-key-actions";

const api = vi.hoisted(() => ({
  rotate: vi.fn(),
  revoke: vi.fn(),
  setPermissions: vi.fn(),
}));
vi.mock("@/entities/service-key", async (original) => {
  const actual = await original<typeof ServiceKeyModule>();
  return { ...actual, serviceKeyApi: { ...actual.serviceKeyApi, ...api } };
});

const service: ServiceKey = {
  code: "orders",
  clientId: "service-orders",
  name: "Заказы",
  issuedAt: "2026-10-03T12:00:00Z",
  rotatedAt: null,
  permissions: ["service.users.read"],
};

describe("ServiceKeyActions", () => {
  beforeEach(() => {
    api.rotate.mockReset().mockResolvedValue({ service, clientSecret: "new-secret" });
    api.revoke.mockReset().mockResolvedValue(null);
    api.setPermissions.mockReset().mockResolvedValue(service);
  });

  it("перевыпуск — после подтверждения; новый секрет в окне, которое не закрыть Esc", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ServiceKeyActions service={service} />);

    await user.click(screen.getByRole("button", { name: "Перевыпустить" }));
    expect(api.rotate).not.toHaveBeenCalled();
    await user.click(
      within(screen.getByRole("dialog")).getByRole("button", { name: "Перевыпустить" }),
    );

    expect(api.rotate).toHaveBeenCalledWith("orders", false);
    expect(await screen.findByTestId("service-secret")).toHaveTextContent("new-secret");
    await user.keyboard("{Escape}");
    expect(screen.getByTestId("service-secret")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Я сохранил ключ" }));
    expect(screen.queryByTestId("service-secret")).toBeNull();
  });

  it("плановый перевыпуск — выданные токены доживают срок", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ServiceKeyActions service={service} />);

    await user.click(screen.getByRole("button", { name: "Перевыпустить" }));
    await user.click(screen.getByRole("checkbox", { name: /Плановая смена/ }));
    await user.click(
      within(screen.getByRole("dialog")).getByRole("button", { name: "Перевыпустить" }),
    );

    expect(api.rotate).toHaveBeenCalledWith("orders", true);
  });

  it("отзыв — только после подтверждения", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ServiceKeyActions service={service} />);

    await user.click(screen.getByRole("button", { name: "Отозвать" }));
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Отмена" }));
    expect(api.revoke).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Отозвать" }));
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Отозвать" }));
    expect(api.revoke).toHaveBeenCalledWith("orders");
  });

  it("права: окно открывается с текущими правами сервиса и сохраняет отмеченный набор", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ServiceKeyActions service={service} />);

    await user.click(screen.getByRole("button", { name: "Права" }));
    expect(screen.getByRole("checkbox", { name: /Читать пользователя/ })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: /Проверять доступ/ })).not.toBeChecked();

    await user.click(screen.getByRole("checkbox", { name: /Читать пользователя/ }));
    await user.click(screen.getByRole("checkbox", { name: /Проверять доступ/ }));
    await user.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(api.setPermissions).toHaveBeenCalledWith("orders", ["service.access.check"]);
  });

  it("права в приложениях проекта — с кодом приложения", async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    queryClient.setQueryData(serviceKeyQueries.grantable().queryKey, [
      {
        app: "notifications",
        name: "Уведомления",
        permissions: [
          {
            key: "notifications:notifications.send",
            name: "notifications.send",
            description: "Отправлять уведомления",
          },
        ],
      },
    ]);
    renderWithProviders(<ServiceKeyActions service={service} />, queryClient);

    await user.click(screen.getByRole("button", { name: "Права" }));
    await user.click(await screen.findByRole("checkbox", { name: /Отправлять уведомления/ }));
    expect(screen.getByText("В приложении «Уведомления»")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(api.setPermissions).toHaveBeenCalledWith("orders", [
      "service.users.read",
      "notifications:notifications.send",
    ]);
  });
});
