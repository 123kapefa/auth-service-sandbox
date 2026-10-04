import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as AppUserModule from "@/entities/app-user";
import { type UserAppRoles } from "@/entities/app-user";
import { renderWithProviders } from "@/test/render";
import { UserRolesEditor } from "../ui/user-roles-editor";

const setRoles = vi.hoisted(() => vi.fn());
vi.mock("@/entities/app-user", async (original) => {
  const actual = await original<typeof AppUserModule>();
  return { ...actual, appUserApi: { ...actual.appUserApi, setRoles } };
});

const data: UserAppRoles = {
  userId: "u1",
  roles: ["manager"],
  defaultRoles: ["customer"],
  modes: ["customer", "manager"],
  activeMode: "customer",
  hasAccess: null,
  assignableRoles: [
    { name: "manager", description: "Менеджер" },
    { name: "admin", description: "Администратор" },
    { name: "support", description: null },
  ],
};

describe("UserRolesEditor", () => {
  beforeEach(() => setRoles.mockReset().mockResolvedValue(data));

  it("роли по умолчанию — отдельно, выданные отмечены, без изменений сохранять нечего", () => {
    renderWithProviders(<UserRolesEditor clientId="sandbox-api" data={data} />);

    expect(screen.getByText("customer")).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: /manager/ })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: /admin/ })).not.toBeChecked();
    expect(screen.getByRole("button", { name: "Сохранить роли" })).toBeDisabled();
  });

  it("сохраняет ровно отмеченный список", async () => {
    const user = userEvent.setup();
    renderWithProviders(<UserRolesEditor clientId="sandbox-api" data={data} />);

    await user.click(screen.getByRole("checkbox", { name: /support/ }));
    await user.click(screen.getByRole("checkbox", { name: /manager/ }));
    await user.click(screen.getByRole("button", { name: "Сохранить роли" }));

    expect(setRoles).toHaveBeenCalledWith("sandbox-api", "u1", ["support"]);
  });
});
