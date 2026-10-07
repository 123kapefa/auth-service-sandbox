import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { type CodeUsage } from "@/entities/sign-in-settings";
import { renderWithProviders } from "@/test/render";
import { UsageCard } from "../ui/usage-card";

const USAGE: CodeUsage = {
  date: "2026-10-07",
  zone: "Europe/Moscow",
  budget: { spent: 150_000, limit: 300_000, alertPercent: 80, exhausted: false },
  call: { sent: 1284, cost: 103_000 },
  sms: { sent: 96, cost: 47_000 },
  email: { sent: 412, cost: 0 },
  sent: 1792,
  verified: 1630,
  verifiedPercent: 91,
};

describe("UsageCard", () => {
  it("звонки, SMS с долей от кодов, почта, доля введённых и бюджет дня", () => {
    renderWithProviders(<UsageCard usage={USAGE} />);

    expect(screen.getByText(/1\s284/)).toBeInTheDocument();
    expect(screen.getByText(/≈ 1\s030 ₽/)).toBeInTheDocument();
    expect(screen.getByText(/≈ 470 ₽ · 5 % от кодов/)).toBeInTheDocument();
    expect(screen.getByText("91 %")).toBeInTheDocument();
    expect(screen.getByText(/1\s500 ₽ из 3\s000 ₽/)).toBeInTheDocument();
    expect(screen.getByRole("progressbar", { name: "Бюджет дня" })).toHaveAttribute(
      "aria-valuenow",
      "50",
    );
  });

  it("бюджет кончился — метка; без бюджета — без полосы; ничего не отправлено — прочерк", () => {
    renderWithProviders(
      <UsageCard
        usage={{
          ...USAGE,
          budget: { ...USAGE.budget, spent: 300_000, exhausted: true },
        }}
      />,
    );
    expect(screen.getByText("SMS выключены до полуночи")).toBeInTheDocument();

    renderWithProviders(
      <UsageCard
        usage={{
          ...USAGE,
          budget: { spent: 0, limit: 0, alertPercent: 80, exhausted: false },
          verifiedPercent: null,
        }}
      />,
    );
    expect(screen.getByText(/без ограничения/)).toBeInTheDocument();
    expect(screen.getAllByRole("progressbar")).toHaveLength(1);
    expect(screen.getByText("—")).toBeInTheDocument();
  });
});
