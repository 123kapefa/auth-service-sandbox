import { describe, expect, it } from "vitest";
import { permissionSchema } from "../model/schemas";

describe("имя права — как на сервере", () => {
  it("право: части через точку", () => {
    expect(permissionSchema.safeParse({ name: "reports.export", description: "" }).success).toBe(
      true,
    );
    expect(permissionSchema.safeParse({ name: "reports", description: "" }).success).toBe(false);
    expect(permissionSchema.safeParse({ name: "Reports.Export", description: "" }).success).toBe(
      false,
    );
  });
});
