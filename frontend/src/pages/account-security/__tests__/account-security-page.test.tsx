import { QueryClient } from "@tanstack/react-query";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Suspense } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { type AccountProfile, accountProfileQueries } from "@/entities/account-profile";
import {
  type AccountSecurity,
  accountSecurityApi,
  accountSecurityQueries,
  type SignInMethods,
} from "@/entities/account-security";
import { renderWithProviders } from "@/test/render";
import { AccountSecurityPage } from "../ui/account-security-page";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const nothing: AccountSecurity = {
  hasPassword: true,
  authenticators: [],
  passkeys: [],
  recoveryCodes: null,
};

const profile: AccountProfile = {
  id: "a2b3805f-2d09-4c35-ab2e-adc8ae75b01c",
  phone: "+79123456789",
  email: "anna@mail.ru",
  emailVerified: true,
  createdAt: "2026-10-01T10:00:00Z",
};

// Всё включено, ключа и приложения нет: пароль и код на телефон не выключить.
const allOn: SignInMethods = {
  methods: [
    {
      method: "password",
      available: true,
      enabled: true,
      disableBlocker: "needs_passkey_or_authenticator",
    },
    {
      method: "phone-code",
      available: true,
      enabled: true,
      disableBlocker: "needs_passkey_or_authenticator",
    },
    { method: "email-code", available: true, enabled: true, disableBlocker: null },
  ],
  hasPasskey: false,
  hasAuthenticator: false,
  hasRecoveryCodes: false,
  linkedProviders: [],
  history: [],
  projectDisabled: [],
};

const passkeys = [
  { id: "key-1", label: "iPhone Анны", createdAt: "2026-10-05T15:32:00Z" },
  { id: "key-2", label: "MacBook", createdAt: "2026-10-05T15:35:00Z" },
];

function renderPage(data: AccountSecurity, methods: SignInMethods = allOn) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(accountSecurityQueries.security().queryKey, data);
  queryClient.setQueryData(accountSecurityQueries.signInMethods().queryKey, methods);
  queryClient.setQueryData(accountProfileQueries.profile().queryKey, profile);
  return renderWithProviders(
    <Suspense>
      <AccountSecurityPage />
    </Suspense>,
    queryClient,
  );
}

const section = (name: string) => within(screen.getByRole("region", { name }));
const href = (link: HTMLElement) => new URL(link.getAttribute("href") ?? "", "http://x");

describe("AccountSecurityPage", () => {
  // Ссылки возвращают на текущую страницу кабинета.
  beforeEach(() => window.history.pushState({}, "", "/account/security"));

  it("ничего не подключено — подключить; коды — только после приложения; удалять нечего", () => {
    renderPage(nothing);

    expect(screen.queryByRole("link", { name: "Удалить" })).toBeNull();
    const totp = href(
      section("Вход с кодом из приложения (2FA)").getByRole("link", { name: "Подключить" }),
    );
    expect(totp.pathname).toBe("/auth/login");
    expect(totp.searchParams.get("action")).toBe("configure-totp");
    expect(totp.searchParams.get("credential")).toBeNull();
    expect(section("Коды восстановления").queryByRole("link", { name: "Создать" })).toBeNull();
    expect(
      section("Коды восстановления").getByText("Сначала подключите приложение-аутентификатор"),
    ).toBeInTheDocument();
    expect(
      href(section("Способы входа").getByRole("link", { name: "Сменить" })).searchParams.get(
        "action",
      ),
    ).toBe("update-password");
    expect(
      href(section("Ключи доступа").getByRole("link", { name: "Добавить" })).searchParams.get(
        "action",
      ),
    ).toBe("register-passkey");
  });

  it("подключённое — подпись и «Удалить» с Id; без подписи — общее название", () => {
    renderPage({
      ...nothing,
      authenticators: [{ id: "otp-1", label: "Телефон", createdAt: "2026-10-01T10:00:00Z" }],
      passkeys: [{ id: "key-1", label: null, createdAt: "2026-10-02T10:00:00Z" }],
      recoveryCodes: { id: "codes-1", createdAt: "2026-10-01T10:00:00Z", remaining: 9, total: 12 },
    });

    const totp = section("Вход с кодом из приложения (2FA)");
    expect(totp.getByText("Телефон")).toBeInTheDocument();
    const remove = href(totp.getByRole("link", { name: "Удалить" }));
    expect(remove.searchParams.get("action")).toBe("delete-credential");
    expect(remove.searchParams.get("credential")).toBe("otp-1");
    expect(remove.searchParams.get("return_to")).toBe("/account/security?action=delete-credential");
    expect(totp.getByRole("link", { name: "Подключить ещё" })).toBeInTheDocument();

    expect(section("Ключи доступа").getByText("Ключ доступа")).toBeInTheDocument();
    expect(
      href(section("Ключи доступа").getByRole("link", { name: "Удалить" })).searchParams.get(
        "credential",
      ),
    ).toBe("key-1");

    const codes = section("Коды восстановления");
    expect(codes.getByText(/Осталось 9 из 12/)).toBeInTheDocument();
    expect(href(codes.getByRole("link", { name: "Удалить" })).searchParams.get("credential")).toBe(
      "codes-1",
    );
    expect(codes.getByRole("link", { name: "Создать новые" })).toBeInTheDocument();
    expect(totp.getByText(/удалите и их/)).toBeInTheDocument();
  });

  it("коды остались без приложения — предупреждение и «Удалить», создать новые нельзя", () => {
    renderPage({
      ...nothing,
      recoveryCodes: { id: "codes-1", createdAt: "2026-10-01T10:00:00Z", remaining: 3, total: 12 },
    });

    const codes = section("Коды восстановления");
    expect(codes.getByRole("alert")).toHaveTextContent("при входе их спрашивают");
    expect(href(codes.getByRole("link", { name: "Удалить" })).searchParams.get("credential")).toBe(
      "codes-1",
    );
    expect(codes.queryByRole("link", { name: "Создать новые" })).toBeNull();
  });

  it("пароля нет — «Задать»", () => {
    renderPage(
      { ...nothing, hasPassword: false },
      {
        ...allOn,
        methods: [
          { method: "password", available: false, enabled: true, disableBlocker: "not_available" },
          ...allOn.methods.slice(1),
        ],
      },
    );
    const methods = section("Способы входа");
    expect(methods.getByRole("link", { name: "Задать" })).toBeInTheDocument();
    expect(methods.getByText("не задан")).toBeInTheDocument();
  });

  it("без ключа и приложения пароль и код на телефон не выключить — объяснение и «Добавить ключ доступа»", () => {
    renderPage(nothing);

    const methods = section("Способы входа");
    expect(methods.getByRole("button", { name: "Выключить: Пароль" })).toBeDisabled();
    expect(methods.getByRole("button", { name: "Выключить: Код на телефон" })).toBeDisabled();
    expect(methods.getByRole("button", { name: "Выключить: Код на почту" })).toBeEnabled();
    expect(methods.getByText("Сначала добавьте надёжный способ.")).toBeInTheDocument();
    expect(
      href(methods.getByRole("link", { name: "Добавить ключ доступа" })).searchParams.get("action"),
    ).toBe("register-passkey");
    expect(
      methods.getByText("+7 912 345-67-89 · звонком, если не дошёл — SMS"),
    ).toBeInTheDocument();
  });

  it("выключить пароль — окно: что останется, «выйти на других» по умолчанию; запрос с ним", async () => {
    const set = vi.spyOn(accountSecurityApi, "setSignInMethod").mockResolvedValue({
      ...allOn,
      hasPasskey: true,
      methods: [
        { method: "password", available: true, enabled: false, disableBlocker: null },
        ...allOn.methods.slice(1),
      ],
    });
    const anchored = allOn.methods.map((item) => ({ ...item, disableBlocker: null }));
    renderPage({ ...nothing, passkeys }, { ...allOn, hasPasskey: true, methods: anchored });

    await userEvent.click(screen.getByRole("button", { name: "Выключить: Пароль" }));

    const dialog = within(screen.getByRole("dialog", { name: "Выключить вход по паролю?" }));
    expect(dialog.getByText("Ключ доступа — iPhone Анны, MacBook")).toBeInTheDocument();
    expect(dialog.getByText("Код на телефон — +7 912 345-67-89")).toBeInTheDocument();
    expect(dialog.getByText("Код на почту — anna@mail.ru")).toBeInTheDocument();
    expect(dialog.getByRole("checkbox")).toBeChecked();

    await userEvent.click(dialog.getByRole("button", { name: "Выключить" }));

    await waitFor(() =>
      expect(set).toHaveBeenCalledWith(
        { method: "password", enabled: false, signOutOtherDevices: true },
        expect.anything(),
      ),
    );
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(section("Способы входа").getByText("выключен")).toBeInTheDocument();
    set.mockRestore();
  });

  it("выключенный пароль — с какого числа и «Включить» без окна", async () => {
    const set = vi.spyOn(accountSecurityApi, "setSignInMethod").mockResolvedValue(allOn);
    renderPage(
      { ...nothing, passkeys },
      {
        ...allOn,
        hasPasskey: true,
        methods: [
          { method: "password", available: true, enabled: false, disableBlocker: null },
          ...allOn.methods.slice(1).map((item) => ({ ...item, disableBlocker: null })),
        ],
        history: [
          { at: "2026-10-05T15:40:00Z", method: "password", enabled: false, byAdmin: false },
        ],
      },
    );

    const methods = section("Способы входа");
    expect(methods.getByText(/«Забыли пароль» тоже не работает/)).toHaveTextContent(
      /^с 05\.10\.2026/,
    );
    await userEvent.click(methods.getByRole("button", { name: "Включить: Пароль" }));

    await waitFor(() =>
      expect(set).toHaveBeenCalledWith({ method: "password", enabled: true }, expect.anything()),
    );
    set.mockRestore();
  });

  it("выключенное в проекте не показываем: пароль, ключи доступа, Яндекс ID", () => {
    renderPage(
      { ...nothing, passkeys },
      {
        ...allOn,
        linkedProviders: ["yandex", "esia"],
        projectDisabled: ["password", "passkey", "yandex"],
      },
    );

    const methods = section("Способы входа");
    expect(methods.queryByText("Пароль")).toBeNull();
    expect(methods.queryByText("Ключ доступа")).toBeNull();
    expect(methods.queryByText("Яндекс ID")).toBeNull();
    expect(methods.getByText("Госуслуги")).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Ключи доступа" })).toBeNull();
  });
});
