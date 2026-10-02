import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { KcPage } from "./kc.gen";

// В Keycloak контекст страницы — window.kcContext. В npm run dev — макет (?page=login.ftl), чтобы смотреть страницы
// без Keycloak.
if (import.meta.env.DEV && window.kcContext === undefined) {
  const { getKcContextMock } = await import("./login/mocks");
  const pageId = new URLSearchParams(window.location.search).get("page") ?? "login.ftl";
  window.kcContext = getKcContextMock({ pageId: pageId as "login.ftl", overrides: {} });
}

const root = document.getElementById("root");
if (!root) {
  throw new Error("Нет элемента #root");
}

createRoot(root).render(
  <StrictMode>
    {window.kcContext ? <KcPage kcContext={window.kcContext} /> : <p>Нет контекста Keycloak.</p>}
  </StrictMode>,
);
