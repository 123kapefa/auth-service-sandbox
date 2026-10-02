import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { adminSessionQueries, type AdminSession } from "@/entities/admin-session";
import { OverviewPage } from "../ui/overview-page";

function renderWith(session: AdminSession) {
  const queryClient = new QueryClient();
  queryClient.setQueryData(adminSessionQueries.me().queryKey, session);
  render(
    <QueryClientProvider client={queryClient}>
      <OverviewPage />
    </QueryClientProvider>,
  );
}

describe("OverviewPage", () => {
  it("админ приложения видит своё приложение и что прав проекта нет", () => {
    renderWith({
      id: "1",
      phone: "+79990000007",
      activeMode: "customer",
      permissions: ["profile.view"],
      apps: [
        {
          clientId: "manager-1c",
          name: "Менеджер 1С",
          permissions: ["roles.manage"],
          viaProject: false,
        },
      ],
    });

    expect(screen.getByText("Менеджер 1С")).toBeInTheDocument();
    expect(screen.getByText(/как админ приложения/)).toBeInTheDocument();
    expect(screen.getByText(/Прав уровня проекта нет/)).toBeInTheDocument();
  });
});
