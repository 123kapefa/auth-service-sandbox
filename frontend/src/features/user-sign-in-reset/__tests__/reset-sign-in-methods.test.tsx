import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type * as UserModule from "@/entities/user";
import { renderWithProviders } from "@/test/render";
import { ResetSignInMethods } from "../ui/reset-sign-in-methods";

const resetSignInMethods = vi.hoisted(() => vi.fn());
vi.mock("@/entities/user", async (original) => {
  const actual = await original<typeof UserModule>();
  return { ...actual, userApi: { ...actual.userApi, resetSignInMethods } };
});

describe("ResetSignInMethods", () => {
  it("после подтверждения — запрос сброса; заблокировано — кнопка неактивна с причиной", async () => {
    resetSignInMethods.mockResolvedValue({ methods: [] });
    const user = userEvent.setup();
    const { unmount } = renderWithProviders(<ResetSignInMethods userId="u1" blocked={null} />);

    await user.click(screen.getByRole("button", { name: "Вернуть способы по умолчанию" }));
    const dialog = await screen.findByRole("dialog", { name: "Вернуть способы по умолчанию?" });
    await user.click(within(dialog).getByRole("button", { name: "Вернуть" }));
    expect(resetSignInMethods).toHaveBeenCalled();
    unmount();

    renderWithProviders(<ResetSignInMethods userId="u1" blocked="Всё и так включено." />);
    expect(screen.getByRole("button", { name: "Вернуть способы по умолчанию" })).toBeDisabled();
    expect(screen.getByText("Всё и так включено.")).toBeInTheDocument();
  });
});
