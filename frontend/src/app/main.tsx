import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./app";
import { applyStoredTheme } from "@/shared/lib/theme";
import "sonner/dist/styles.css";
import "./styles.css";

const root = document.getElementById("root");
if (!root) {
  throw new Error("Нет элемента #root в index.html");
}

applyStoredTheme();
createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
