import { z } from "zod";

/** Как на сервере: строчная латиница, цифры, дефис; с буквы, не кончается дефисом; до 40 символов. */
export const SERVICE_CODE_PATTERN = /^[a-z][a-z0-9-]*[a-z0-9]$/;

export const serviceKeySchema = z.object({
  code: z
    .string()
    .max(40, "Не больше 40 символов")
    .regex(SERVICE_CODE_PATTERN, "Строчная латиница, цифры и дефис, например orders"),
  name: z.string().trim().min(1, "Нужно название").max(100, "Не больше 100 символов"),
  permissions: z.array(z.string()),
});

export type ServiceKeyForm = z.infer<typeof serviceKeySchema>;
