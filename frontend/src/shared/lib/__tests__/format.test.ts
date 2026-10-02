import { describe, expect, it } from "vitest";
import { formatDateTime } from "../format";

describe("formatDateTime", () => {
  it("нет даты или начало эпохи (импорт realm) — «—»", () => {
    expect(formatDateTime(null)).toBe("—");
    expect(formatDateTime("1970-01-01T00:00:00+00:00")).toBe("—");
  });

  it("дата — по-русски", () => {
    expect(formatDateTime("2026-10-02T10:30:00Z")).toMatch(/02\.10\.2026/);
  });
});
