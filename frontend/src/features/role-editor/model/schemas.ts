import { z } from "zod";

/** Как на сервере: роль — строчная латиница, цифры, дефис, 2–50 символов (с точкой — это права). */
export const ROLE_NAME_PATTERN = /^[a-z][a-z0-9-]{1,49}$/;

export const roleSchema = z.object({
  name: z.string().regex(ROLE_NAME_PATTERN, "Строчная латиница, цифры, дефис; 2–50 символов"),
  description: z.string().max(255, "Не больше 255 символов"),
  permissions: z.array(z.string()),
});

export type RoleForm = z.infer<typeof roleSchema>;
