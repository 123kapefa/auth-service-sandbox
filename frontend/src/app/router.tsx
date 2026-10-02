import { createBrowserRouter } from "react-router";
import { NotFoundPage } from "@/pages/not-found";
import { OverviewPage } from "@/pages/overview";
import { ADMIN_BASE } from "@/shared/config/routes";
import { SessionGate } from "./session-gate";

export const router = createBrowserRouter(
  [
    {
      element: <SessionGate />,
      children: [
        { index: true, element: <OverviewPage /> },
        { path: "*", element: <NotFoundPage /> },
      ],
    },
  ],
  { basename: ADMIN_BASE },
);
