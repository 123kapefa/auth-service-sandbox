import { QueryClient } from "@tanstack/react-query";
import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { adminSessionQueries, type AdminSession } from "@/entities/admin-session";
import { renderWithProviders } from "@/test/render";
import { OverviewPage } from "../ui/overview-page";

function renderWith(session: AdminSession) {
  const queryClient = new QueryClient();
  queryClient.setQueryData(adminSessionQueries.me().queryKey, session);
  renderWithProviders(<OverviewPage />, queryClient);
}

describe("OverviewPage", () => {
  it("админ приложения видит своё приложение ссылкой и что прав проекта нет", () => {
    renderWith({
      id: "1",
      phone: "+79990000007",
      activeMode: "customer",
      permissions: ["profile.view"],
      apps: [
        {
          clientId: "manager-1c",
          name: "Менеджер 1С",
          permissions: ["users.roles.manage"],
          viaProject: false,
        },
      ],
    });

    expect(screen.getByRole("link", { name: "Менеджер 1С" })).toHaveAttribute(
      "href",
      "/apps/manager-1c",
    );
    expect(screen.getByText(/как админ приложения/)).toBeInTheDocument();
    expect(screen.getByText(/Прав уровня проекта нет/)).toBeInTheDocument();
  });
});
