import { afterEach, describe, expect, it } from "vitest";
import { accountActionUrl } from "@/shared/config/routes";
import { accountApp, rememberAccountApp } from "../account-app";
import { currentTheme, setDefaultTheme } from "../theme";

describe("кабинет, открытый из приложения", () => {
  afterEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    window.history.replaceState(null, "", "/");
    setDefaultTheme(null);
  });

  it("app из адреса запоминается на вкладку и убирается из адреса", () => {
    window.history.replaceState(null, "", "/account/security?app=manager-1c&x=1");

    expect(rememberAccountApp()).toBe("manager-1c");
    expect(window.location.pathname + window.location.search).toBe("/account/security?x=1");
    expect(accountApp()).toBe("manager-1c");
  });

  it("неверный app не запоминается", () => {
    window.history.replaceState(null, "", "/account/?app=%3Cscript%3E");

    expect(rememberAccountApp()).toBeNull();
    expect(window.location.search).toBe("");
  });

  it("страница действия — в оформлении того же приложения", () => {
    sessionStorage.setItem("account-app", "manager-1c");

    expect(accountActionUrl("update-password", "/account/")).toBe(
      "/auth/login?return_to=%2Faccount%2F&action=update-password&app=manager-1c",
    );
  });

  it("тема проекта — по умолчанию, выбор пользователя — важнее", () => {
    setDefaultTheme("dark");
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(currentTheme()).toBe("dark");

    localStorage.setItem("theme", "light");
    setDefaultTheme("dark");
    expect(currentTheme()).toBe("light");
  });
});
