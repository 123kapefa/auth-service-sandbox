import { describe, expect, it } from "vitest";
import { formatAgo, formatDateTime } from "../format";

describe("formatDateTime", () => {
  it("нет даты или начало эпохи (импорт realm) — «—»", () => {
    expect(formatDateTime(null)).toBe("—");
    expect(formatDateTime("1970-01-01T00:00:00+00:00")).toBe("—");
  });

  it("дата — по-русски", () => {
    expect(formatDateTime("2026-10-02T10:30:00Z")).toMatch(/02\.10\.2026/);
  });
});

describe("formatAgo", () => {
  const now = new Date("2026-10-04T12:00:00Z").getTime();

  it("минуты и часы назад, старше суток — дата", () => {
    expect(formatAgo("2026-10-04T11:59:40Z", now)).toBe("только что");
    expect(formatAgo("2026-10-04T11:55:00Z", now)).toBe("5 мин назад");
    expect(formatAgo("2026-10-04T09:00:00Z", now)).toBe("3 ч назад");
    expect(formatAgo("2026-10-02T10:30:00Z", now)).toMatch(/02\.10\.2026/);
  });
});
