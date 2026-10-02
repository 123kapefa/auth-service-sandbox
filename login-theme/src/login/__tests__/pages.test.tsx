import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import KcPage from "../KcPage";
import { getKcContextMock } from "../mocks";

describe("страница входа", () => {
  it("«Телефон или почта», пароль, «Войти по коду» — и номер с «+»", async () => {
    render(<KcPage kcContext={getKcContextMock({ pageId: "login.ftl", overrides: {} })} />);

    const login = await screen.findByLabelText("Телефон или почта");
    expect(screen.getByLabelText("Пароль")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Войти по коду" })).toHaveAttribute(
      "name",
      "codeLogin",
    );

    await userEvent.type(login, "79990000001");
    expect(login).toHaveValue("+79990000001");
  });

  it("на сервер уходит логин: номер цифрами, почта как есть", async () => {
    const { container } = render(
      <KcPage kcContext={getKcContextMock({ pageId: "login.ftl", overrides: {} })} />,
    );
    const login = await screen.findByLabelText("Телефон или почта");
    const sent = () => container.querySelector<HTMLInputElement>('input[name="username"]')?.value;

    await userEvent.type(login, "8 (999) 000-00-01");
    expect(sent()).toBe("89990000001");

    await userEvent.clear(login);
    await userEvent.type(login, "user@mail.ru");
    expect(sent()).toBe("user@mail.ru");
  });
});

describe("вход по коду", () => {
  it("куда отправлен код и через сколько можно повторить", async () => {
    render(
      <KcPage kcContext={getKcContextMock({ pageId: "sandbox-code-login.ftl", overrides: {} })} />,
    );

    await waitFor(() =>
      expect(screen.getByText("Отправили код в SMS на +7 999 *** ** 01.")).toBeInTheDocument(),
    );
    expect(screen.getByLabelText("Код из SMS")).toHaveAttribute("autocomplete", "one-time-code");
    expect(screen.getByText("Новый код можно запросить через 42 с.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Отправить код ещё раз" })).toHaveAttribute(
      "name",
      "resend",
    );
  });

  it("письмом — свои подписи", async () => {
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
  });
});
