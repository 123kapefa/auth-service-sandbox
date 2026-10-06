import { QueryClientProvider } from "@tanstack/react-query";
import { Suspense } from "react";
import { RouterProvider } from "react-router";
import { createQueryClient } from "@/shared/api";
import { PageLoading, Toaster } from "@/shared/ui";
import { router } from "./router";

const queryClient = createQueryClient();

export function AccountApp() {
  return (
    <QueryClientProvider client={queryClient}>
      <Suspense fallback={<PageLoading />}>
        <RouterProvider router={router} />
      </Suspense>
      <Toaster />
    </QueryClientProvider>
  );
}
