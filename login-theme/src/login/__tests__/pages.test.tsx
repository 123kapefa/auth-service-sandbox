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

describe("вход в два шага", () => {
  const page = (overrides: Record<string, unknown>) => (
    <KcPage kcContext={getKcContextMock({ pageId: "login.ftl", overrides })} />
  );

  it("шаг 1: только телефон или почта и «Продолжить»; внешний вход тут же", async () => {
    render(page({ loginStep: "login" }));

    expect(await screen.findByLabelText("Телефон или почта")).toBeInTheDocument();
    expect(screen.queryByLabelText("Пароль")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Войти по коду" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Продолжить" })).toHaveAttribute("name", "next");
    expect(screen.getByRole("link", { name: "Яндекс ID" })).toBeInTheDocument();
  });

  it("шаг 2 с паролем: что ввели, «Изменить», пароль сразу и код на то, что ввели", async () => {
    render(
      page({ loginStep: "password", loginMethods: "phone-code", loginDisplay: "+79990000001" }),
    );

    expect(await screen.findByLabelText("Пароль")).toBeInTheDocument();
    expect(screen.getByText("Введите пароль")).toBeInTheDocument();
    expect(screen.getByText("+7 (999) 000-00-01")).toBeInTheDocument();
    const change = screen.getByRole("button", { name: "Изменить" });
    expect(change).toHaveAttribute("name", "changeLogin");
    // Enter в поле пароля жмёт первую кнопку формы пароля: «Войти», а «Изменить» — в своей форме.
    const passwordForm = (screen.getByLabelText("Пароль") as HTMLInputElement).form;
    expect((change as HTMLButtonElement).form).not.toBe(passwordForm);
    // Кнопка по умолчанию — первая кнопка отправки, принадлежащая форме (form.elements, как у браузера).
    const submits = Array.from(passwordForm?.elements ?? []).filter(
      (element) => element instanceof HTMLButtonElement && element.type === "submit",
    );
    expect(submits[0]).toHaveAttribute("name", "login");
    expect(screen.queryByLabelText("Телефон или почта")).not.toBeInTheDocument();
    // Ввели номер — «Звонок»: код уходит из формы пароля, но пароль не нужен.
    const code = screen.getByRole("button", { name: "Звонок" });
    expect(code).toHaveAttribute("name", "codeLogin");
    expect(code).toHaveAttribute("value", "phone-code");
    expect((code as HTMLButtonElement).form).toBe(passwordForm);
    expect(screen.getByRole("link", { name: "Забыли пароль?" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Яндекс ID" })).not.toBeInTheDocument();
  });

  it("шаг 2 с паролем, ввели почту — «Код на почту»", async () => {
    render(
      page({ loginStep: "password", loginMethods: "email-code", loginDisplay: "a@sandbox.test" }),
    );

    const code = await screen.findByRole("button", { name: "Код на почту" });
    expect(code).toHaveAttribute("value", "email-code");
    expect(screen.queryByRole("button", { name: "Звонок" })).not.toBeInTheDocument();
  });

  it("в проекте только телефон или только почта — подпись поля", async () => {
    const { unmount } = render(page({ loginStep: "login", loginKinds: "phone" }));
    expect(await screen.findByLabelText("Номер телефона")).toBeInTheDocument();
    unmount();

    render(page({ loginStep: "login", loginKinds: "email" }));
    expect(await screen.findByLabelText("Почта")).toBeInTheDocument();
  });

  it("шаг 2 без пароля: способы по цене, почта и номер скрыты частично", async () => {
    render(
      page({
        loginStep: "methods",
        loginMethods: "email-code,phone-code",
        loginDisplay: "+79990000001",
        loginEmailHint: "s***@sandbox.test",
        loginPhoneHint: "+7 *** ***-**-01",
      }),
    );

    expect(await screen.findByText("Как войти?")).toBeInTheDocument();
    expect(screen.queryByLabelText("Пароль")).not.toBeInTheDocument();
    const buttons = screen.getAllByRole("button", { name: /^(Код на почту|Звонок)/ });
    expect(buttons.map((button) => button.textContent)).toEqual([
      "@Код на почтуна s***@sandbox.test",
      "☎Звонокна +7 *** ***-**-01 — код в номере звонящего",
    ]);
    expect(buttons.map((button) => button.getAttribute("value"))).toEqual([
      "email-code",
      "phone-code",
    ]);
  });

  it("шаг 2 без пароля и без других способов — войти через Яндекс ID или Госуслуги", async () => {
    render(page({ loginStep: "methods", loginMethods: "", loginDisplay: "a@sandbox.test" }));

    expect(await screen.findByText(/Войдите через Яндекс ID или Госуслуги/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Госуслуги" })).toBeInTheDocument();
  });
});

describe("внешний вход", () => {
  it("после «или»: ключ доступа, Госуслуги и Яндекс ID — ссылки на вход через провайдера", async () => {
    render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "login.ftl",
          overrides: { loginStep: "login", enableWebAuthnConditionalUI: true } as Record<
            string,
            unknown
          >,
        })}
      />,
    );

    expect(await screen.findByText("или")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Войти с ключом доступа" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Госуслуги" })).toHaveAttribute("href", "#esia");
    expect(screen.getByRole("link", { name: "Яндекс ID" })).toHaveAttribute("href", "#yandex");
  });

  it("подтверждение пароля при привязке (логин известен) — только пароль: без кода и внешнего входа", async () => {
    render(
      <KcPage
        kcContext={getKcContextMock({ pageId: "login.ftl", overrides: { usernameHidden: true } })}
      />,
    );

    expect(await screen.findByLabelText("Пароль")).toBeInTheDocument();
    expect(screen.queryByLabelText("Телефон или почта")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Войти по коду" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Госуслуги" })).not.toBeInTheDocument();
  });
});

describe("вход по коду", () => {
  afterEach(() => vi.useRealTimers());

  it("куда отправлен код; «ещё раз» — после паузы, отсчёт идёт", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "platform-code-login.ftl",
          overrides: { codeResendAfter: "2" },
        })}
      />,
    );

    await waitFor(() =>
      expect(
        screen.getByText("Отправили SMS с 6 цифрами на +7 999 *** ** 01."),
      ).toBeInTheDocument(),
    );
    expect(screen.getByRole("heading", { name: "Код из SMS" })).toBeInTheDocument();
    expect(screen.getByLabelText("Код из SMS")).toHaveAttribute("autocomplete", "one-time-code");
    expect(screen.getByRole("link", { name: "Изменить" })).toBeInTheDocument();
    expect(screen.getByText("Отправить ещё раз — через 0:02")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Отправить ещё раз" })).toBeNull();

    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(screen.getByText("Отправить ещё раз — через 0:01")).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(screen.queryByText(/через 0:0/)).toBeNull();
    expect(screen.getByRole("button", { name: "Отправить ещё раз" })).toHaveAttribute(
      "name",
      "resend",
    );
  });

  it("звонок: «Вам звонят», 4 цифры; после паузы — «Звонок не пришёл?», «Прислать SMS» и «Позвонить ещё раз»", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "platform-code-login.ftl",
          overrides: {
            codeDelivery: "CALL",
            codeSmsAvailable: "true",
            codeLength: "4",
            codeResendAfter: "1",
          },
        })}
      />,
    );

    expect(await screen.findByText("Вам звонят")).toBeInTheDocument();
    expect(screen.getByText(/последние 4 цифры/)).toBeInTheDocument();
    expect(screen.getByLabelText("Последние 4 цифры номера")).toBeInTheDocument();
    expect(screen.getByText("Позвонить ещё раз или прислать SMS — через 0:01")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Прислать SMS" })).toBeNull();

    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(screen.getByText("Звонок не пришёл?")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Прислать SMS" })).toHaveAttribute("name", "sendSms");
    expect(screen.getByRole("button", { name: "Позвонить ещё раз" })).toHaveAttribute(
      "name",
      "resend",
    );
  });

  it("SMS у проекта выключены — на экране звонка нет «Прислать SMS»", async () => {
    render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "platform-code-login.ftl",
          overrides: {
            codeDelivery: "CALL",
            codeSmsAvailable: "false",
            codeLength: "4",
            codeResendAfter: "0",
          },
        })}
      />,
    );
    await screen.findByRole("button", { name: "Позвонить ещё раз" });
    expect(screen.getByText(/не блокирует незнакомые номера\.$/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Прислать SMS" })).toBeNull();
  });

  it("в поле — только цифры и не больше длины кода; набрали все — форма уходит сама", async () => {
    const submit = vi
      .spyOn(HTMLFormElement.prototype, "requestSubmit")
      .mockImplementation(() => undefined);
    render(
      <KcPage kcContext={getKcContextMock({ pageId: "platform-code-login.ftl", overrides: {} })} />,
    );
    const code = await screen.findByLabelText("Код из SMS");

    await userEvent.type(code, "12a3");
    expect(code).toHaveValue("123");
    expect(screen.getByRole("button", { name: "Войти" })).toBeDisabled();
    expect(submit).not.toHaveBeenCalled();

    await userEvent.type(code, "45678");
    expect(code).toHaveValue("123456");
    await waitFor(() => expect(submit).toHaveBeenCalledOnce());
    submit.mockRestore();
  });

  // Без maxLength у поля: браузер обрезал бы вставку до фильтра, и «352 948» стало бы «35294».
  it("вставили код с пробелом и текстом — берутся цифры, форма уходит сама", async () => {
    const submit = vi
      .spyOn(HTMLFormElement.prototype, "requestSubmit")
      .mockImplementation(() => undefined);
    render(
      <KcPage kcContext={getKcContextMock({ pageId: "platform-code-login.ftl", overrides: {} })} />,
    );
    const code = await screen.findByLabelText("Код из SMS");

    await userEvent.click(code);
    await userEvent.paste("Код: 352 948");
    expect(code).toHaveValue("352948");
    await waitFor(() => expect(submit).toHaveBeenCalledOnce());
    submit.mockRestore();
  });

  it("письмом — свои подписи; паузы нет — кнопка «ещё раз» доступна", async () => {
    render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "platform-code-login.ftl",
          overrides: {
            codeChannel: "EMAIL",
            codeDelivery: "EMAIL",
            codeTarget: "c***@sandbox.test",
            codeResendAfter: "0",
          },
        })}
      />,
    );

    await waitFor(() =>
      expect(screen.getByText(/Письмо может прийти в «Спам»/)).toBeInTheDocument(),
    );
    expect(screen.getByRole("heading", { name: "Код из письма" })).toBeInTheDocument();
    expect(screen.getByLabelText("Код из письма")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Отправить ещё раз" })).toBeEnabled();
  });
});

describe("капча перед кодом", () => {
  it("«Проверка»: галочка заглушки стенда даёт smart-token, без неё «Позвонить» недоступна", async () => {
    const { container } = render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "platform-code-login.ftl",
          overrides: {
            codeDelivery: "CALL",
            codeLength: "4",
            captchaRequired: "true",
            captchaStep: "true",
            captchaSiteKey: "sandbox-stub",
            message: { type: "error", summary: "Подтвердите, что вы не робот" },
          },
        })}
      />,
    );

    expect(await screen.findByRole("heading", { name: "Проверка" })).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
    const call = screen.getByRole("button", { name: "Позвонить" });
    expect(call).toBeDisabled();
    expect(call).toHaveAttribute("name", "resend");

    await userEvent.click(screen.getByLabelText(/Я не робот/));
    expect(call).toBeEnabled();
    expect(container.querySelector<HTMLInputElement>('input[name="smart-token"]')?.value).toBe(
      "sandbox-captcha-ok",
    );
    expect((call as HTMLButtonElement).form).toBe(
      container.querySelector('input[name="smart-token"]')?.closest("form"),
    );
  });
});

describe("«Забыли пароль?»", () => {
  it("номер — той же маской, на сервер уходит цифрами с 7", async () => {
    const { container } = render(
      <KcPage
        kcContext={getKcContextMock({ pageId: "login-reset-password.ftl", overrides: {} })}
      />,
    );
    const login = await screen.findByLabelText("Телефон или почта");

    await userEvent.type(login, "8 999 000-00-05");
    expect(login).toHaveValue("+7 (999) 000-00-05");
    expect(container.querySelector<HTMLInputElement>('input[name="username"]')?.value).toBe(
      "79990000005",
    );
  });
});

describe("отправка формы", () => {
  // Заблокированная кнопка в отправку не попадает: блокировка — только после того, как браузер собрал данные.
  it("«Войти по коду» попадает в отправку, кнопки блокируются после", async () => {
    render(<KcPage kcContext={getKcContextMock({ pageId: "login.ftl", overrides: {} })} />);
    const code = await screen.findByRole("button", { name: "Войти по коду" });

    act(() => code.click());
    expect(code).toBeEnabled();
    await waitFor(() => expect(code).toBeDisabled());
  });

  it("«Отправить код ещё раз» попадает в отправку", async () => {
    render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "platform-code-login.ftl",
          overrides: { codeResendAfter: "0" },
        })}
      />,
    );
    const resend = await screen.findByRole("button", { name: "Отправить ещё раз" });

    act(() => resend.click());
    expect(resend).toBeEnabled();
    await waitFor(() => expect(resend).toBeDisabled());
  });
});

describe("стандартные страницы в нашем каркасе", () => {
  it("2FA: «Код из приложения», ячейки кода и «Подтвердить»", async () => {
    render(<KcPage kcContext={getKcContextMock({ pageId: "login-otp.ftl", overrides: {} })} />);
    expect(await screen.findByRole("heading", { name: "Код из приложения" })).toBeInTheDocument();
    expect(screen.getByLabelText("Одноразовый код")).toHaveAttribute("name", "otp");
    expect(screen.getByRole("button", { name: "Подтвердить" })).toBeDisabled();
  });

  it("выбор способа входа — по-русски, без английских названий", async () => {
    render(
      <KcPage
        kcContext={getKcContextMock({ pageId: "select-authenticator.ftl", overrides: {} })}
      />,
    );
    expect(await screen.findByText("Приложение-аутентификатор")).toBeInTheDocument();
    expect(screen.queryByText(/Authenticator Application/)).not.toBeInTheDocument();
  });

  it("коды восстановления — в списке с id, который тема оформляет без нумерации", async () => {
    const { container } = render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "login-recovery-authn-code-config.ftl",
          overrides: {},
        })}
      />,
    );
    await screen.findByText(/code-123/);
    expect(container.querySelector("#kc-recovery-codes-list")).not.toBeNull();
  });
});

describe("смена номера", () => {
  const page = (overrides: Record<string, unknown>) => (
    <KcPage kcContext={getKcContextMock({ pageId: "platform-update-phone.ftl", overrides })} />
  );

  it("новый номер: нынешний номер, поле, «Получить код» и «Отмена»", async () => {
    render(page({ phoneStep: "phone", phoneCurrent: "+79990000001" }));

    expect(await screen.findByText("Новый номер")).toBeInTheDocument();
    expect(screen.getByText("+7 (999) 000-00-01")).toBeInTheDocument();
    const phone = screen.getByLabelText("Новый номер телефона");
    expect(phone).toHaveAttribute("name", "phone");
    expect(screen.getByRole("button", { name: "Получить код" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Отмена" })).toHaveAttribute("name", "cancel-aia");

    await userEvent.type(phone, "89990000002");
    expect(phone).toHaveValue("+7 (999) 000-00-02");
  });

  it("меняли меньше суток назад — без поля, с причиной", async () => {
    render(
      page({
        phoneStep: "phone",
        phoneNextAllowed: "2026-10-07T12:00:00Z",
        message: {
          type: "error",
          summary: "Номер можно менять раз в сутки. Снова — примерно через 5 ч.",
        },
      }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent("раз в сутки");
    expect(screen.queryByLabelText("Новый номер телефона")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Получить код" })).not.toBeInTheDocument();
  });

  it("код: экран звонка, «Сменить номер», «Изменить» — другой номер, «Отмена»", async () => {
    render(
      page({
        phoneStep: "code",
        codeChannel: "SMS",
        codeDelivery: "CALL",
        codeSmsAvailable: "true",
        codeTarget: "+79990000002",
        codeResendAfter: "30",
        codeLength: "4",
      }),
    );

    expect(await screen.findByText("+7 (999) 000-00-02")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Сменить номер" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Изменить" })).toHaveAttribute("name", "changePhone");
    expect(screen.getByRole("button", { name: "Отмена" })).toHaveAttribute("name", "cancel-aia");
    expect(screen.queryByText("Выбрать другой способ")).not.toBeInTheDocument();
  });
});

describe("предложение ключа доступа", () => {
  it("после входа по коду: «Входите без кода», «Добавить ключ доступа», «Не сейчас»", async () => {
    render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "webauthn-register.ftl",
          overrides: { platformPasskeyOffer: "true" },
        })}
      />,
    );

    expect(await screen.findByText("Входите без кода")).toBeInTheDocument();
    expect(screen.getByText(/Face ID, отпечатку или PIN/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Добавить ключ доступа" })).toHaveAttribute(
      "type",
      "button",
    );
    expect(screen.getByRole("button", { name: "Не сейчас" })).toHaveAttribute("name", "skip");
    expect(screen.getByText(/Спросим снова через 30 дней/)).toBeInTheDocument();
    expect(screen.queryByLabelText(/Выполнить выход/)).not.toBeInTheDocument();
    expect(document.getElementById("register")).toContainElement(
      document.getElementById("clientDataJSON"),
    );
  });

  it("переходный период: какой способ выключат и когда, спросим при следующем входе", async () => {
    render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "webauthn-register.ftl",
          overrides: {
            platformPasskeyOffer: "true",
            platformSignInTransition: "phone:2026-11-05",
          } as { platformPasskeyOffer?: string; platformSignInTransition?: string },
        })}
      />,
    );

    expect(await screen.findByText("Добавьте другой способ входа")).toBeInTheDocument();
    expect(
      screen.getByText(/Вход по телефону в проекте выключится после 05\.11\.2026/),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Не сейчас" })).toHaveAttribute("name", "skip");
    expect(screen.getByText(/Спросим при следующем входе/)).toBeInTheDocument();
    expect(screen.queryByText(/через 30 дней/)).not.toBeInTheDocument();
  });

  it("не получилось: «Попробовать ещё раз» и «Не сейчас»", async () => {
    render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "webauthn-error.ftl",
          overrides: { platformPasskeyOffer: "true" },
        })}
      />,
    );

    expect(await screen.findByRole("alert")).toHaveTextContent("Не получилось добавить ключ");
    const retry = screen.getByRole("button", { name: "Попробовать ещё раз" });
    expect(retry.closest("form")?.elements.namedItem("isSetRetry")).toHaveValue("retry");
    expect(screen.getByRole("button", { name: "Не сейчас" })).toHaveAttribute("name", "skip");
  });

  it("из кабинета: «Ключ доступа», «Добавить ключ доступа» и «Отмена», без «Не сейчас»", async () => {
    render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "webauthn-register.ftl",
          overrides: { isAppInitiatedAction: true },
        })}
      />,
    );

    expect(await screen.findByText(/без пароля и кода/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Добавить ключ доступа" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Отмена" })).toHaveAttribute("name", "cancel-aia");
    expect(screen.queryByRole("button", { name: "Не сейчас" })).not.toBeInTheDocument();
    expect(screen.queryByText(/Спросим снова/)).not.toBeInTheDocument();
    const logout = screen.getByLabelText("Выполнить выход на других устройствах");
    expect(logout).toHaveAttribute("name", "logout-sessions");
    expect(logout).not.toBeChecked();
    expect(logout.closest("form")?.id).toBe("register");
  });

  it("ошибка ключа при входе — стандартная страница", async () => {
    render(
      <KcPage
        kcContext={getKcContextMock({
          pageId: "webauthn-error.ftl",
          overrides: { isAppInitiatedAction: false },
        })}
      />,
    );

    await waitFor(() => expect(document.getElementById("kc-try-again")).toBeInTheDocument());
    expect(screen.queryByText(/Не получилось добавить ключ/)).not.toBeInTheDocument();
  });
});
