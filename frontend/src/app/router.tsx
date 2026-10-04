import { createBrowserRouter } from "react-router";
import { AppJournalPage } from "@/pages/app-journal";
import { AppRolesPage } from "@/pages/app-roles";
import { AppUserPage } from "@/pages/app-user";
import { AppUsersPage } from "@/pages/app-users";
import { BrandingPage } from "@/pages/branding";
import { JournalPage } from "@/pages/journal";
import { NotFoundPage } from "@/pages/not-found";
import { OverviewPage } from "@/pages/overview";
import { ServicesPage } from "@/pages/services";
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
        { path: "journal", element: <JournalPage /> },
        { path: "services", element: <ServicesPage /> },
        { path: "branding", element: <BrandingPage /> },
        {
          path: "apps/:clientId",
          element: <AppSection />,
          children: [
            { index: true, element: <AppSectionIndex /> },
            { path: "users", element: <AppUsersPage /> },
            { path: "users/:userId", element: <AppUserPage /> },
            { path: "roles", element: <AppRolesPage /> },
            { path: "journal", element: <AppJournalPage /> },
          ],
        },
        { path: "*", element: <NotFoundPage /> },
      ],
    },
  ],
  { basename: ADMIN_BASE },
);
