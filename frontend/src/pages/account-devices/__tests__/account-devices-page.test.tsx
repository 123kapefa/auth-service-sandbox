import { QueryClient } from "@tanstack/react-query";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Suspense } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  type AccountSession,
  accountDevicesQueries,
  type LoginAttempt,
} from "@/entities/account-devices";
import { apiClient } from "@/shared/api";
import { renderWithProviders } from "@/test/render";
import { AccountDevicesPage } from "../ui/account-devices-page";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const session = (id: string, deviceLabel: string | null, isCurrent = false): AccountSession => ({
  id,
  userAgent: null,
  ipAddress: "10.0.0.1",
  deviceLabel,
  createdAt: "2026-10-04T09:00:00Z",
  lastSeenAt: "2026-10-04T10:00:00Z",
  isCurrent,
  apps: ["sandbox-bff"],
});

const history: LoginAttempt[] = [
  {
    time: "2026-10-04T09:00:00Z",
    success: true,
    error: null,
    ipAddress: "10.0.0.1",
    deviceLabel: "Safari · iPhone",
    sessionId: "s-phone",
  },
  {
    time: "2026-10-03T22:05:00Z",
    success: false,
    error: "invalid_user_credentials",
    ipAddress: "10.0.0.2",
    deviceLabel: null,
    sessionId: null,
  },
];

function renderPage(sessions: AccountSession[], logins: LoginAttempt[] = history) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(accountDevicesQueries.sessions().queryKey, sessions);
  queryClient.setQueryData(accountDevicesQueries.logins().queryKey, logins);
  return renderWithProviders(
    <Suspense>
      <AccountDevicesPage />
    </Suspense>,
    queryClient,
  );
}

const sessionsCard = () => within(screen.getByRole("region", { name: "Где вы вошли" }));

describe("AccountDevicesPage", () => {
  afterEach(() => vi.restoreAllMocks());

  it("сессии: это устройство без кнопки, другие — «Выйти»; неузнанное — «Неизвестное устройство»", () => {
    renderPage([
      session("s-current", "Chrome · macOS", true),
      session("s-phone", "Safari · iPhone"),
      session("s-old", null),
    ]);

    const card = sessionsCard();
    expect(card.getByText("Chrome · macOS")).toBeInTheDocument();
    expect(card.getByText("[ это устройство ]")).toBeInTheDocument();
    expect(card.getByRole("button", { name: "Выйти: Safari · iPhone" })).toBeInTheDocument();
    expect(card.getByRole("button", { name: "Выйти: Неизвестное устройство" })).toBeInTheDocument();
    expect(card.queryByRole("button", { name: "Выйти: Chrome · macOS" })).toBeNull();
  });

  it("«Выйти» на устройстве — завершает именно его сессию", async () => {
    const post = vi.spyOn(apiClient, "post").mockResolvedValue({ data: {} });
    renderPage([
      session("s-current", "Chrome · macOS", true),
      session("s-phone", "Safari · iPhone"),
    ]);

    await userEvent.click(sessionsCard().getByRole("button", { name: "Выйти: Safari · iPhone" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith("/security/sessions/s-phone/terminate"));
  });

  it("только это устройство — «Выйти на других» выключена", () => {
    renderPage([session("s-current", "Chrome · macOS", true)]);

    expect(screen.getByRole("button", { name: "Выйти на других" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Выйти везде" })).toBeEnabled();
  });

  it("«Выйти везде» — после подтверждения: другие сессии, затем эта", async () => {
    const post = vi
      .spyOn(apiClient, "post")
      .mockResolvedValue({ data: { result: { terminated: 1 }, error: null, isError: false } });
    const assign = vi.fn();
    vi.spyOn(window, "location", "get").mockReturnValue({
      ...window.location,
      assign,
    });
    renderPage([
      session("s-current", "Chrome · macOS", true),
      session("s-phone", "Safari · iPhone"),
    ]);

    await userEvent.click(screen.getByRole("button", { name: "Выйти везде" }));
    expect(post).not.toHaveBeenCalled();
    await userEvent.click(
      within(screen.getByRole("dialog")).getByRole("button", { name: "Выйти везде" }),
    );

    await waitFor(() => expect(assign).toHaveBeenCalledWith("/"));
    expect(post.mock.calls.map(([url]) => url)).toEqual([
      "/security/sessions/terminate-all-others",
      "/auth/logout",
    ]);
  });

  it("история: удачный вход с устройством, ошибка — с причиной по-русски", () => {
    renderPage([session("s-current", "Chrome · macOS", true)]);

    const rows = within(screen.getByRole("region", { name: "История входов" })).getAllByRole("row");
    expect(rows).toHaveLength(3);
    expect(rows[1]).toHaveTextContent("Safari · iPhone");
    expect(rows[1]).toHaveTextContent("успешно");
    expect(rows[2]).toHaveTextContent("неверный пароль или код");
    expect(rows[2]).toHaveTextContent("10.0.0.2");
  });
});
