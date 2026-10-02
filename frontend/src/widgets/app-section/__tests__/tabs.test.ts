import { describe, expect, it } from "vitest";
import { appTabs } from "../model/tabs";

const app = (permissions: string[]) => ({
  clientId: "manager-1c",
  name: null,
  permissions,
  viaProject: false,
});

describe("appTabs", () => {
  it("вкладки — по правам в приложении", () => {
    expect(appTabs(app(["users.roles.manage", "roles.manage"])).map((tab) => tab.to)).toEqual([
      "users",
      "roles",
    ]);
    expect(appTabs(app(["roles.manage"])).map((tab) => tab.to)).toEqual(["roles"]);
    expect(appTabs(app(["users.block"]))).toEqual([]);
  });
});
