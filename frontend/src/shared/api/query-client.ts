import { QueryClient } from "@tanstack/react-query";
import { AxiosError } from "axios";
import { EnvelopeError } from "./errors";

// Ошибки сервиса (Envelope) и отказы доступа повторять бессмысленно; сеть — до 3 раз.
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 60_000,
        gcTime: 5 * 60_000,
        refetchOnWindowFocus: false,
        retry: (failureCount, error) =>
          !(error instanceof EnvelopeError) &&
          !(error instanceof AxiosError && error.response) &&
          failureCount < 3,
      },
      mutations: { retry: false },
    },
  });
}
