import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { renderWithProviders } from "@/test/render";
import { Sidebar, SidebarGroup, SidebarLink } from "../sidebar";

describe("Sidebar", () => {
  it("на узком экране меню свёрнуто под «☰», раскрывается и сворачивается после выбора раздела", async () => {
    renderWithProviders(
      <Sidebar title="Auth Sandbox" footer={<span>подвал</span>}>
        <SidebarGroup>
          <SidebarLink to="/profile" number={1}>
            Профиль
          </SidebarLink>
        </SidebarGroup>
      </Sidebar>,
    );

    const toggle = screen.getByRole("button", { name: "Меню" });
    const menu = screen.getByRole("navigation", { name: "Разделы" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(menu).toHaveClass("hidden");

    await userEvent.click(toggle);
    expect(screen.getByRole("button", { name: "Свернуть меню" })).toHaveAttribute(
      "aria-expanded",
      "true",
    );
    expect(menu).not.toHaveClass("hidden");

    await userEvent.click(screen.getByRole("link", { name: /Профиль/ }));
    expect(menu).toHaveClass("hidden");
  });
});
