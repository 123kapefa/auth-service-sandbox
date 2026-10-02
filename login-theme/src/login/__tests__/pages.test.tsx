import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import KcPage from "../KcPage";
import { getKcContextMock } from "../mocks";

describe("страница входа", () => {
  it("«Телефон или почта», пароль, «Войти по коду», «Показать» — и номер с маской +7", async () => {
    render(<KcPage kcContext={getKcContextMock({ pageId: "login.ftl", overrides: {} })} />);

    const login = await screen.findByLabelText("Телефон или почта");
    expect(screen.getByLabelText("Пароль")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Войти по коду" })).toHaveAttribute(
      "name",
      "codeLogin",
    );
    expect(screen.getByRole("button", { name: "Показать" })).toBeInTheDocument();

    await userEvent.type(login, "89990000001");
    expect(login).toHaveValue("+7 (999) 000-00-01");
  });

  it("на сервер уходит логин: номер цифрами с 7, почта как есть", async () => {
    const { container } = render(
      <KcPage kcContext={getKcContextMock({ pageId: "login.ftl", overrides: {} })} />,
    );
    const login = await screen.findByLabelText("Телефон или почта");
    const sent = () => container.querySelector<HTMLInputElement>('input[name="username"]')?.value;

    await userEvent.type(login, "9990000001");
    expect(sent()).toBe("79990000001");

    await userEvent.clear(login);
    await userEvent.type(login, "user@mail.ru");
    expect(sent()).toBe("user@mail.ru");
  });

  it("неверный логин или пароль — ошибка у полей, без общей плашки", async () => {
    render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "login.ftl",
          overrides: {
            login: { username: "79990000001" },
            message: { type: "error", summary: "Неправильное имя пользователя или пароль." },
            messagesPerField: {
              existsError: (...fields: string[]) =>
                fields.includes("username") || fields.includes("password"),
              get: () => "Неправильное имя пользователя или пароль.",
              getFirstError: () => "Неправильное имя пользователя или пароль.",
              exists: () => true,
              printIfExists: <T,>(_field: string, text: T) => text,
            },
          },
        })}
      />,
    );

    const login = await screen.findByLabelText("Телефон или почта");
    expect(login).toHaveValue("+7 (999) 000-00-01");
    expect(login).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText("Пароль")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getAllByRole("alert")).toHaveLength(1);
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Неправильное имя пользователя или пароль.",
    );
  });
});

describe("вход по коду", () => {
  afterEach(() => vi.useRealTimers());

  it("куда отправлен код; «ещё раз» — после паузы, отсчёт идёт", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "sandbox-code-login.ftl",
          overrides: { codeResendAfter: "2" },
        })}
      />,
    );

    await waitFor(() =>
      expect(screen.getByText("Отправили код в SMS на +7 999 *** ** 01.")).toBeInTheDocument(),
    );
    expect(screen.getByLabelText("Код из SMS")).toHaveAttribute("autocomplete", "one-time-code");
    expect(screen.getByText("Новый код можно запросить через 2 с.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Отправить код ещё раз" })).toBeDisabled();

    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(screen.getByText("Новый код можно запросить через 1 с.")).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(screen.queryByText(/Новый код можно запросить/)).toBeNull();
    expect(screen.getByRole("button", { name: "Отправить код ещё раз" })).toBeEnabled();
  });

  it("в поле — только цифры и не больше длины кода; набрали все — форма уходит сама", async () => {
    const submit = vi
      .spyOn(HTMLFormElement.prototype, "requestSubmit")
      .mockImplementation(() => undefined);
    render(
      <KcPage kcContext={getKcContextMock({ pageId: "sandbox-code-login.ftl", overrides: {} })} />,
    );
    const code = await screen.findByLabelText("Код из SMS");

    await userEvent.type(code, "12a3");
    expect(code).toHaveValue("123");
    expect(screen.getByRole("button", { name: "Вход" })).toBeDisabled();
    expect(submit).not.toHaveBeenCalled();

    await userEvent.type(code, "45678");
    expect(code).toHaveValue("123456");
    await waitFor(() => expect(submit).toHaveBeenCalledOnce());
    submit.mockRestore();
  });

  it("письмом — свои подписи; паузы нет — кнопка «ещё раз» доступна", async () => {
    render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "sandbox-code-login.ftl",
          overrides: {
            codeChannel: "EMAIL",
            codeTarget: "c***@sandbox.test",
            codeResendAfter: "0",
          },
        })}
      />,
    );

    await waitFor(() =>
      expect(screen.getByText("Отправили код письмом на c***@sandbox.test.")).toBeInTheDocument(),
    );
    expect(screen.getByLabelText("Код из письма")).toBeInTheDocument();
    expect(screen.queryByText(/Новый код можно запросить/)).toBeNull();
    expect(screen.getByRole("button", { name: "Отправить код ещё раз" })).toBeEnabled();
  });
});
