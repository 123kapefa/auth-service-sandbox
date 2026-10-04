import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import { type ReactNode } from "react";
import { MemoryRouter } from "react-router";

/**
 * Отрисовать с QueryClient (без повторов) и роутером; queryClient — чтобы заранее положить данные, route — адрес
 * страницы (с параметрами).
 */
export function renderWithProviders(
  ui: ReactNode,
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
  route = "/",
) {
  return {
    queryClient,
    ...render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
      </QueryClientProvider>,
    ),
  };
}
