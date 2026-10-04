import { QueryClientProvider } from "@tanstack/react-query";
import { Suspense } from "react";
import { RouterProvider } from "react-router";
import { createQueryClient } from "@/shared/api";
import { Toaster } from "@/shared/ui";
import { router } from "./router";

const queryClient = createQueryClient();

export function AccountApp() {
  return (
    <QueryClientProvider client={queryClient}>
      <Suspense
        fallback={<p className="p-6 font-mono text-[13px] text-muted-foreground">Загрузка…</p>}
      >
        <RouterProvider router={router} />
      </Suspense>
      <Toaster />
    </QueryClientProvider>
  );
}
