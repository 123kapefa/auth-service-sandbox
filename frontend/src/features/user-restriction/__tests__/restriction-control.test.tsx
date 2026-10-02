import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { type UserDetail } from "@/entities/user";
import type * as SharedApi from "@/shared/api";
import { renderWithProviders } from "@/test/render";
import { blockSchema } from "../model/schemas";
import { RestrictionControl } from "../ui/restriction-control";

const put = vi.hoisted(() => vi.fn());
vi.mock("@/shared/api", async (original) => {
  const actual = await original<typeof SharedApi>();
  return { ...actual, apiClient: { ...actual.apiClient, put } };
});

const user: UserDetail = {
  id: "u1",
  username: "79990000001",
  phone: "+79990000001",
  email: null,
  role: "customer",
  createdAt: "2026-01-01T00:00:00Z",
  isRestricted: false,
  restrictionReason: null,
  restrictedAt: null,
  availableRoles: ["customer"],
  activeMode: "customer",
  hasPassword: true,
  lastLoginAt: null,
  hasTwoFactor: false,
};

describe("блокировка", () => {
  beforeEach(() => put.mockReset().mockResolvedValue({}));

  it("причина — не длиннее 500 символов, как на сервере", () => {
    expect(blockSchema.safeParse({ reason: "x".repeat(500) }).success).toBe(true);
    expect(blockSchema.safeParse({ reason: "x".repeat(501) }).success).toBe(false);
  });

  it("блокирует с причиной", async () => {
    const actor = userEvent.setup();
    renderWithProviders(<RestrictionControl user={user} />);

    await actor.click(screen.getByRole("button", { name: "Заблокировать" }));
    await actor.type(screen.getByLabelText(/Причина/), "спам");
    await actor.click(
      within(screen.getByRole("dialog")).getByRole("button", { name: "Заблокировать" }),
    );

    expect(put).toHaveBeenCalledWith("/auth/admin/users/u1/restriction", {
      restricted: true,
      reason: "спам",
    });
  });
});
