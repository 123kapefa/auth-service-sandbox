import { z } from "zod";

/** Как на сервере: части через точку, строчная латиница и цифры (reports.export); до 100 символов. */
export const PERMISSION_NAME_PATTERN = /^[a-z][a-z0-9]*(\.[a-z0-9][a-z0-9_-]*)+$/;

export const permissionSchema = z.object({
  name: z
    .string()
    .max(100, "Не больше 100 символов")
    .regex(
      PERMISSION_NAME_PATTERN,
      "Части через точку: строчная латиница и цифры, например reports.export",
    ),
  description: z.string().max(255, "Не больше 255 символов"),
});

export type PermissionForm = z.infer<typeof permissionSchema>;
