import { z } from "zod";

/** Как на сервере: причина — до 500 символов, необязательна. */
export const MAX_REASON_LENGTH = 500;

export const blockSchema = z.object({
  reason: z.string().max(MAX_REASON_LENGTH, `Не больше ${MAX_REASON_LENGTH} символов`),
});

export type BlockForm = z.infer<typeof blockSchema>;
