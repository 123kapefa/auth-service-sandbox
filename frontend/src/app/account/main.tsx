import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { AccountApp } from "./app";
import { rememberAccountApp } from "@/shared/lib/account-app";
import { applyStoredTheme } from "@/shared/lib/theme";
import "sonner/dist/styles.css";
import "../styles.css";

const root = document.getElementById("root");
if (!root) {
  throw new Error("Нет элемента #root в account.html");
}

applyStoredTheme();
rememberAccountApp();
createRoot(root).render(
  <StrictMode>
    <AccountApp />
  </StrictMode>,
);
