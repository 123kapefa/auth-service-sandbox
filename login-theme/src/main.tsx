import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { KcPage } from "./kc.gen";
import { applyBranding, loadBranding } from "./login/branding";

// В Keycloak контекст страницы — window.kcContext. В npm run dev — макет (?page=login.ftl), чтобы смотреть страницы
// без Keycloak; &overrides={…} (JSON) — поля контекста поверх макета (шаг входа, способ кода, сообщение).
if (import.meta.env.DEV && window.kcContext === undefined) {
  const { getKcContextMock } = await import("./login/mocks");
  const search = new URLSearchParams(window.location.search);
  const pageId = search.get("page") ?? "login.ftl";
  const overrides = JSON.parse(search.get("overrides") ?? "{}") as object;
  window.kcContext = getKcContextMock({ pageId: pageId as "login.ftl", overrides });
}

// Оформление проекта (и приложения) — до первого показа, чтобы страница не мигнула серой (не дождались — серая).
if (window.kcContext) {
  const { realm, client, url } = window.kcContext;
  const branding = await loadBranding(realm.name, {
    clientId: client?.clientId,
    loginAction: url?.loginAction,
  });
  if (branding) {
    applyBranding(branding);
  }
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
