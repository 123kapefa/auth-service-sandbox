import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { type AdminSession } from "@/entities/admin-session";
import { renderWithProviders } from "@/test/render";
import { AppShell } from "../ui/app-shell";

const session: AdminSession = {
  id: "1",
  phone: "+79990000001",
  activeMode: "admin",
  permissions: ["users.view", "audit.view"],
  apps: [
    {
      clientId: "sandbox-api",
      name: "Песочница API",
      permissions: ["users.roles.manage", "roles.manage"],
      viaProject: true,
    },
    { clientId: "manager-1c", name: "Менеджер 1С", permissions: ["audit.view"], viaProject: true },
  ],
};

describe("AppShell", () => {
  it("боковое меню: разделы проекта, приложения и вкладки открытого приложения под ним", () => {
    renderWithProviders(<AppShell session={session} />, undefined, "/apps/sandbox-api/roles");
    const nav = screen.getByRole("navigation", { name: "Разделы" });

    expect(within(nav).getByRole("link", { name: /Обзор/ })).toHaveAttribute("href", "/");
    expect(within(nav).getByRole("link", { name: /Песочница API/ })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(within(nav).getByRole("link", { name: "Роли и права" })).toHaveAttribute(
      "href",
      "/apps/sandbox-api/roles",
    );
    expect(within(nav).getByRole("link", { name: "Пользователи" })).toHaveAttribute(
      "href",
      "/apps/sandbox-api/users",
    );
    // Вкладки закрытого приложения не показываются.
    expect(within(nav).queryByRole("link", { name: "Журнал" })).not.toBeInTheDocument();
    expect(screen.getByText("режим: admin")).toBeInTheDocument();
  });
});
