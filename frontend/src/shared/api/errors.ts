import { AxiosError } from "axios";
import { type EnvelopeErrorBody } from "./envelope";

/** Ошибка из Envelope: код (users.self_role…), сообщение сервиса, HTTP-статус. */
export class EnvelopeError extends Error {
  readonly code: string;
  readonly type: EnvelopeErrorBody["type"];
  readonly status: number;
  readonly invalidField: string | null;

  constructor(body: EnvelopeErrorBody, status: number) {
    const first = body.messages[0];
    super(first?.message ?? "Ошибка сервиса");
    this.name = "EnvelopeError";
    this.code = first?.code ?? "unknown";
    this.type = body.type;
    this.status = status;
    this.invalidField = first?.invalidField ?? null;
  }
}

/** Код ошибки сервиса или null (сеть, неожиданный ответ). */
export function getErrorCode(error: unknown): string | null {
  return error instanceof EnvelopeError ? error.code : null;
}

/** Сообщение для пользователя: от сервиса или запасное. */
export function getErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof EnvelopeError) {
    return error.message;
  }

  if (error instanceof AxiosError && !error.response) {
    return "Сервис недоступен — проверьте соединение";
  }

  return fallback;
}
