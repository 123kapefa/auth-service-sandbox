import { createBrowserRouter } from "react-router";
import { AppRolesPage } from "@/pages/app-roles";
import { AppUserPage } from "@/pages/app-user";
import { AppUsersPage } from "@/pages/app-users";
import { NotFoundPage } from "@/pages/not-found";
import { OverviewPage } from "@/pages/overview";
import { UserPage } from "@/pages/user";
import { UsersPage } from "@/pages/users";
import { ADMIN_BASE } from "@/shared/config/routes";
import { AppSection, AppSectionIndex } from "@/widgets/app-section";
import { SessionGate } from "./session-gate";

export const router = createBrowserRouter(
  [
    {
      element: <SessionGate />,
      children: [
        { index: true, element: <OverviewPage /> },
        { path: "users", element: <UsersPage /> },
        { path: "users/:userId", element: <UserPage /> },
        {
          path: "apps/:clientId",
          element: <AppSection />,
          children: [
            { index: true, element: <AppSectionIndex /> },
            { path: "users", element: <AppUsersPage /> },
            { path: "users/:userId", element: <AppUserPage /> },
            { path: "roles", element: <AppRolesPage /> },
          ],
        },
        { path: "*", element: <NotFoundPage /> },
      ],
    },
  ],
  { basename: ADMIN_BASE },
);
