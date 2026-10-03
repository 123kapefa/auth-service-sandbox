import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as ServiceKeyModule from "@/entities/service-key";
import { renderWithProviders } from "@/test/render";
import { serviceKeySchema } from "../model/schemas";
import { IssueServiceKeyButton } from "../ui/issue-service-key-button";

const issue = vi.hoisted(() => vi.fn());
vi.mock("@/entities/service-key", async (original) => {
  const actual = await original<typeof ServiceKeyModule>();
  return { ...actual, serviceKeyApi: { ...actual.serviceKeyApi, issue } };
});

describe("код сервиса — как на сервере", () => {
  it("строчная латиница, цифры и дефис, с буквы", () => {
    const valid = (code: string) =>
      serviceKeySchema.safeParse({ code, name: "x", permissions: [] }).success;
    expect(valid("orders")).toBe(true);
    expect(valid("order-events2")).toBe(true);
    expect(valid("Orders")).toBe(false);
    expect(valid("1orders")).toBe(false);
    expect(valid("orders-")).toBe(false);
    expect(valid("заказы")).toBe(false);
  });
});

describe("IssueServiceKeyButton", () => {
  beforeEach(() =>
    issue.mockReset().mockResolvedValue({
      service: {
        code: "orders",
        clientId: "service-orders",
        name: "Заказы",
        issuedAt: "2026-10-03T12:00:00Z",
        rotatedAt: null,
        permissions: ["service.users.read"],
      },
      clientSecret: "s3cr3t-value",
    }),
  );

  it("выдаёт ключ с отмеченными правами и показывает секрет один раз", async () => {
    const user = userEvent.setup();
    renderWithProviders(<IssueServiceKeyButton />);

    await user.click(screen.getByRole("button", { name: "Добавить сервис" }));
    await user.type(screen.getByLabelText("Код"), "orders");
    await user.type(screen.getByLabelText("Название"), "Заказы");
    await user.click(screen.getByRole("checkbox", { name: /Читать пользователя/ }));
    await user.click(screen.getByRole("button", { name: "Выдать ключ" }));

    expect(issue).toHaveBeenCalledWith({
      code: "orders",
      name: "Заказы",
      permissions: ["service.users.read"],
    });
    expect(await screen.findByTestId("service-secret")).toHaveTextContent("s3cr3t-value");
    expect(screen.getByText("service-orders")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Я сохранил ключ" }));
    expect(screen.queryByTestId("service-secret")).toBeNull();
  });

  it("неверный код — ошибка у поля, запроса нет", async () => {
    const user = userEvent.setup();
    renderWithProviders(<IssueServiceKeyButton />);

    await user.click(screen.getByRole("button", { name: "Добавить сервис" }));
    await user.type(screen.getByLabelText("Код"), "Заказы");
    await user.type(screen.getByLabelText("Название"), "Заказы");
    await user.click(screen.getByRole("button", { name: "Выдать ключ" }));

    expect(await screen.findByText(/Строчная латиница, цифры и дефис/)).toBeInTheDocument();
    expect(issue).not.toHaveBeenCalled();
  });
});
