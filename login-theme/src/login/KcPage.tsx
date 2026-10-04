import type { ClassKey } from "keycloakify/login";
import DefaultPage from "keycloakify/login/DefaultPage";
import { lazy, Suspense } from "react";
import { classes } from "./classes";
import { useI18n } from "./i18n";
import type { KcContext } from "./KcContext";
import "./styles.css";
import Template from "./Template";

const UserProfileFormFields = lazy(() => import("keycloakify/login/UserProfileFormFields"));
const CodeLogin = lazy(() => import("./pages/CodeLogin"));
const Login = lazy(() => import("./pages/Login"));
const LoginResetPassword = lazy(() => import("./pages/LoginResetPassword"));

/** Страница по pageId Keycloak: свои — свёрстаны в pages/, остальные — стандартные Keycloakify в нашем каркасе. */
export default function KcPage({ kcContext }: { kcContext: KcContext }) {
  const { i18n } = useI18n({ kcContext });

  return (
    <Suspense>
      {(() => {
        switch (kcContext.pageId) {
          case "login.ftl":
            return (
              <Login
                kcContext={kcContext}
                i18n={i18n}
                classes={pageClasses}
                Template={Template}
                doUseDefaultCss={false}
              />
            );
          case "login-reset-password.ftl":
            return (
              <LoginResetPassword
                kcContext={kcContext}
                i18n={i18n}
                classes={pageClasses}
                Template={Template}
                doUseDefaultCss={false}
              />
            );
          case "platform-code-login.ftl":
            return (
              <CodeLogin
                kcContext={kcContext}
                i18n={i18n}
                classes={pageClasses}
                Template={Template}
                doUseDefaultCss={false}
              />
            );
          default:
            return (
              <DefaultPage
                kcContext={kcContext}
                i18n={i18n}
                classes={pageClasses}
                Template={Template}
                doUseDefaultCss={false}
                UserProfileFormFields={UserProfileFormFields}
                doMakeUserConfirmPassword
              />
            );
        }
      })()}
    </Suspense>
  );
}

const pageClasses: { [key in ClassKey]?: string } = classes;
