import { QueryClient } from "@tanstack/react-query";
import { screen, within } from "@testing-library/react";
import { Route, Routes } from "react-router";
import { describe, expect, it } from "vitest";
import { appCatalogQueries } from "@/entities/app-catalog";
import { renderWithProviders } from "@/test/render";
import { AppRolesPage } from "../ui/app-roles-page";

describe("AppRolesPage", () => {
  it("встроенную роль и системное право удалить нельзя; свои — можно", () => {
    const queryClient = new QueryClient();
    queryClient.setQueryData(appCatalogQueries.roles("").queryKey, [
      { name: "admin", description: "Админ", builtin: true, permissions: ["roles.manage"] },
      { name: "logist", description: null, builtin: false, permissions: [] },
    ]);
    queryClient.setQueryData(appCatalogQueries.permissions("").queryKey, [
      { name: "roles.manage", description: null, source: "code" },
      { name: "orders.export", description: null, source: "admin" },
    ]);

    renderWithProviders(
      <Routes>
        <Route path="*" element={<AppRolesPage />} />
      </Routes>,
      queryClient,
    );

    const item = (text: string) => {
      const found = screen.getAllByText(text)[0]?.closest("li");
      if (!found) {
        throw new Error(`Нет строки «${text}»`);
      }
      return found as HTMLElement;
    };
    expect(within(item("admin")).queryByRole("button", { name: "Удалить" })).toBeNull();
    expect(within(item("logist")).getByRole("button", { name: "Удалить" })).toBeInTheDocument();
    expect(
      within(item("orders.export")).getByRole("button", { name: "Удалить" }),
    ).toBeInTheDocument();
    expect(within(item("без прав")).getByText("logist")).toBeInTheDocument();
  });
});
