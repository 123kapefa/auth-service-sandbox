import { createBrowserRouter } from "react-router";
import { AccountProfilePage } from "@/pages/account-profile";
import { NotFoundPage } from "@/pages/not-found";
import { ACCOUNT_BASE } from "@/shared/config/routes";
import { AccountGate } from "./account-gate";

export const router = createBrowserRouter(
  [
    {
      element: <AccountGate />,
      children: [
        { index: true, element: <AccountProfilePage /> },
        { path: "*", element: <NotFoundPage /> },
      ],
    },
  ],
  { basename: ACCOUNT_BASE },
);
