import { QueryClientProvider } from "@tanstack/react-query";
import { Suspense } from "react";
import { RouterProvider } from "react-router";
import { Toaster } from "sonner";
import { createQueryClient } from "@/shared/api";
import { listenAccessChanges } from "./access-changed-toast";
import { router } from "./router";

const queryClient = createQueryClient();
listenAccessChanges(queryClient);

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <Suspense fallback={<p className="p-6 text-sm text-muted-foreground">Загрузка…</p>}>
        <RouterProvider router={router} />
      </Suspense>
      <Toaster richColors position="top-right" />
    </QueryClientProvider>
  );
}
