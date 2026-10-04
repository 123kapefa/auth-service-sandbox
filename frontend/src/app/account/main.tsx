import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { AccountApp } from "./app";
import "sonner/dist/styles.css";
import "../styles.css";

const root = document.getElementById("root");
if (!root) {
  throw new Error("Нет элемента #root в account.html");
}

createRoot(root).render(
  <StrictMode>
    <AccountApp />
  </StrictMode>,
);
