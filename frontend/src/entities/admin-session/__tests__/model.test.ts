import { describe, expect, it } from "vitest";
import { accessOf, type AdminSession } from "@/entities/admin-session";

const session = (permissions: string[], apps: AdminSession["apps"] = []): AdminSession => ({
  id: "1",
  phone: "+70000000000",
  activeMode: "admin",
  permissions,
  apps,
});

describe("accessOf", () => {
  it("админ проекта: пользователи и журнал проекта", () => {
    const access = accessOf(session(["users.view", "audit.view"]));

    expect(access).toMatchObject({ projectUsers: true, projectAudit: true, any: true });
  });

  it("админ приложения: только его приложение", () => {
    const app = {
      clientId: "manager-1c",
      name: "Менеджер 1С",
      permissions: ["roles.manage"],
      viaProject: false,
    };

    const access = accessOf(session(["profile.view"], [app]));

    expect(access).toMatchObject({
      projectUsers: false,
      projectAudit: false,
      any: true,
      apps: [app],
    });
  });

  it("покупатель: делать в админке нечего", () => {
    expect(accessOf(session(["profile.view"])).any).toBe(false);
  });
});
