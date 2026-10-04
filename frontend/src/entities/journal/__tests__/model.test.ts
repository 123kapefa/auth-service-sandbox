import { describe, expect, it } from "vitest";
import { actionLabel, describeChanges } from "../model";

describe("describeChanges", () => {
  it("{ old, new } — «было → стало», приложение не повторяется", () => {
    expect(
      describeChanges({ app: "manager-1c", role: { old: "customer", new: "manager,support" } }),
    ).toEqual(["role: customer → manager,support"]);
  });

  it("списки, флаги и пустые значения — читаемо", () => {
    expect(
      describeChanges({
        permissions: { old: ["a.b"], new: [] },
        hasAccess: { old: true, new: false },
        email: "x@y.z",
      }),
    ).toEqual(["permissions: a.b → —", "hasAccess: да → нет", "email: x@y.z"]);
  });
});

describe("actionLabel", () => {
  it("известное — по-русски, неизвестное — как есть", () => {
    expect(actionLabel("UserRoleChanged")).toBe("Роли пользователя");
    expect(actionLabel("SomethingNew")).toBe("SomethingNew");
  });
});
