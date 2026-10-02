import { describe, expect, it } from "vitest";
import { accessOf, type AdminSession } from "@/entities/admin-session";
import { navItems } from "../model/nav";

const app = (clientId: string, permissions: string[], viaProject = false) => ({
  clientId,
  name: clientId,
  permissions,
  viaProject,
});
const labels = (session: AdminSession) => navItems(accessOf(session)).map((item) => item.label);
const session = (permissions: string[], apps: AdminSession["apps"] = []): AdminSession => ({
  id: "1",
  phone: null,
  activeMode: null,
  permissions,
  apps,
});

describe("navItems", () => {
  it("админ проекта: пользователи и все приложения", () => {
    const admin = session(
      ["users.view", "users.roles.manage"],
      [
        app("manager-1c", ["users.roles.manage"], true),
        app("sandbox-api", ["users.roles.manage"], true),
      ],
    );

    expect(labels(admin)).toEqual(["Обзор", "Пользователи", "manager-1c", "sandbox-api"]);
  });

  it("админ приложения: только своё приложение", () => {
    expect(labels(session(["profile.view"], [app("manager-1c", ["users.roles.manage"])]))).toEqual([
      "Обзор",
      "manager-1c",
    ]);
  });

  it("приложение, где можно только менять роли, — тоже в меню", () => {
    expect(labels(session(["profile.view"], [app("manager-1c", ["roles.manage"])]))).toEqual([
      "Обзор",
      "manager-1c",
    ]);
  });

  it("менеджер: пользователи; приложение без ролей и их выдачи в меню не попадает", () => {
    expect(
      labels(session(["users.view", "users.block"], [app("sandbox-api", ["users.block"], true)])),
    ).toEqual(["Обзор", "Пользователи"]);
  });
});
