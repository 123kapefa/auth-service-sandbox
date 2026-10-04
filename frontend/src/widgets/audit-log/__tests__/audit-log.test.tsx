import { QueryClient } from "@tanstack/react-query";
import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { type AuditPage, journalQueries } from "@/entities/journal";
import { renderWithProviders } from "@/test/render";
import { AuditLog } from "../ui/audit-log";

const page: AuditPage = {
  page: 1,
  pageSize: 25,
  hasMore: false,
  items: [
    {
      id: "1",
      occurredAt: "2026-10-02T10:00:00Z",
      action: "UserRoleChanged",
      objectType: "user",
      objectId: "u1",
      objectName: "79990000008",
      changes: { app: "manager-1c", role: { old: "operator", new: "accountant" } },
      actorId: "a1",
      actorName: "+79990000007",
      actorRole: "customer",
      correlationId: null,
      app: "manager-1c",
    },
  ],
};

const filter = { action: "", app: "", from: "", to: "", objectId: "", page: 1 };

function renderLog(clientId: string | null) {
  const queryClient = new QueryClient();
  queryClient.setQueryData(journalQueries.audit(clientId, filter).queryKey, page);
  renderWithProviders(
    <AuditLog clientId={clientId} apps={[{ clientId: "manager-1c", name: "Менеджер 1С" }]} />,
    queryClient,
  );
}

describe("AuditLog", () => {
  it("журнал проекта: кто, в каком режиме, действие по-русски, изменения, приложение", () => {
    renderLog(null);

    expect(screen.getByText("Роли пользователя", { selector: "td" })).toBeInTheDocument();
    expect(screen.getByText("role: operator → accountant")).toBeInTheDocument();
    expect(screen.getByText("режим customer")).toBeInTheDocument();
    expect(screen.getByLabelText("Приложение")).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Приложение" })).toBeInTheDocument();
  });

  it("журнал приложения: без фильтра и колонки приложения", () => {
    renderLog("manager-1c");

    expect(screen.queryByLabelText("Приложение")).toBeNull();
    expect(screen.queryByRole("columnheader", { name: "Приложение" })).toBeNull();
    expect(screen.getByText("role: operator → accountant")).toBeInTheDocument();
  });
});
