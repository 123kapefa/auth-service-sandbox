import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as AppCatalogModule from "@/entities/app-catalog";
import { type AppPermission, type AppRole } from "@/entities/app-catalog";
import { renderWithProviders } from "@/test/render";
import { roleSchema } from "../model/schemas";
import { RoleDialog } from "../ui/role-dialog";

const api = vi.hoisted(() => ({ createRole: vi.fn(), updateRole: vi.fn() }));
vi.mock("@/entities/app-catalog", async (original) => {
  const actual = await original<typeof AppCatalogModule>();
  return { ...actual, appCatalogApi: { ...actual.appCatalogApi, ...api } };
});

const permissions: AppPermission[] = [
  { name: "orders.view", description: "Заказы", source: "code" },
  { name: "orders.export", description: null, source: "admin" },
];
const role: AppRole = {
  name: "logist",
  description: "Логист",
  builtin: false,
  permissions: ["orders.view"],
};

describe("имя роли — как на сервере", () => {
  it("роль: строчная латиница, цифры, дефис; без точки", () => {
    expect(
      roleSchema.safeParse({ name: "support-2", description: "", permissions: [] }).success,
    ).toBe(true);
    expect(
      roleSchema.safeParse({ name: "Support", description: "", permissions: [] }).success,
    ).toBe(false);
    expect(
      roleSchema.safeParse({ name: "support.team", description: "", permissions: [] }).success,
    ).toBe(false);
  });
});

describe("RoleDialog", () => {
  beforeEach(() => {
    api.createRole.mockReset().mockResolvedValue([]);
    api.updateRole.mockReset().mockResolvedValue([]);
  });

  it("изменение роли — описание и ровно отмеченные права", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <RoleDialog
        clientId="manager-1c"
        role={role}
        permissions={permissions}
        open
        onOpenChange={() => undefined}
      />,
    );
    const dialog = screen.getByRole("dialog");

    await user.click(within(dialog).getByRole("checkbox", { name: /orders\.export/ }));
    await user.click(within(dialog).getByRole("checkbox", { name: /orders\.view/ }));
    await user.click(within(dialog).getByRole("button", { name: "Сохранить" }));

    expect(api.updateRole).toHaveBeenCalledWith("manager-1c", {
      name: "logist",
      description: "Логист",
      permissions: ["orders.export"],
    });
  });

  it("новая роль с неверным именем — ошибка у поля, запроса нет", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <RoleDialog
        clientId="manager-1c"
        role={null}
        permissions={permissions}
        open
        onOpenChange={() => undefined}
      />,
    );

    await user.type(screen.getByLabelText("Имя"), "Support");
    await user.click(screen.getByRole("button", { name: "Создать" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/Строчная латиница/);
    expect(api.createRole).not.toHaveBeenCalled();
  });
});
