/** Ответ сервиса — Envelope стандарта платформы. */
export type ErrorType =
  "VALIDATION" | "NOT_FOUND" | "FAILURE" | "CONFLICT" | "AUTHENTICATION" | "AUTHORIZATION";

export interface ErrorMessage {
  code: string;
  message: string;
  invalidField?: string | null;
}

export interface EnvelopeErrorBody {
  messages: ErrorMessage[];
  type: ErrorType;
}

export interface Envelope<T> {
  result: T | null;
  error: EnvelopeErrorBody | null;
  isError: boolean;
  timeGenerated?: string;
}
