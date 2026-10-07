package platform.keycloak.auth;

import jakarta.ws.rs.core.MultivaluedHashMap;
import jakarta.ws.rs.core.MultivaluedMap;
import jakarta.ws.rs.core.Response;

import java.util.HashSet;
import java.util.Locale;
import java.util.Optional;
import java.util.Set;

import org.keycloak.WebAuthnConstants;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.authenticators.browser.UsernamePasswordForm;
import org.keycloak.forms.login.LoginFormsProvider;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.ModelDuplicateException;
import org.keycloak.models.UserModel;
import org.keycloak.models.utils.KeycloakModelUtils;
import org.keycloak.events.Details;
import org.keycloak.events.Errors;
import org.keycloak.representations.idm.CredentialRepresentation;
import org.keycloak.services.managers.AuthenticationManager;
import org.keycloak.services.validation.Validation;

/**
 * Вход в два шага (10.4) на основе встроенного UsernamePasswordForm, страница — login.ftl:
 * <ol>
 *   <li>«Телефон или почта» → «Продолжить» (кнопка next). Ключ доступа, Яндекс ID и Госуслуги — тут же, без логина.</li>
 *   <li>Чем войти — по LoginSteps: поле пароля сразу (и для незнакомого логина) или список включённых способов,
 *       если человек выключил пароль. Один способ — открывается сам. «Изменить» (changeLogin) — назад к шагу 1.</li>
 * </ol>
 * Шаг страницы — атрибут loginStep (login, password, methods), способы — loginMethods. Введённый логин хранится в
 * заметке сессии входа; пароль проверяется именно для него.
 *
 * Номер приводится к логину на сервере: «+7 (999) 000-00-01», «8 999 000 00 01» и «9990000001» — один пользователь
 * 79990000001. Вход по паролю выключен у человека (SignInMethods) — отказ, как на неверный пароль.
 *
 * «Войти по коду» (codeLogin): номер или почта запоминается в сессии входа, дальше — подсценарий «вход по коду»
 * (CodeRequestedCondition → CodeLoginAuthenticator). На шаге «способы» значение кнопки — какой код (email-code,
 * phone-code), адрес — из аккаунта.
 *
 * Запрос в один шаг (логин с паролем или с codeLogin сразу) тоже принимается: так шлют менеджеры паролей и скрипты.
 */
public final class PasswordLoginForm extends UsernamePasswordForm {

    static final String BUTTON_CODE_LOGIN = "codeLogin";
    static final String BUTTON_CHANGE_LOGIN = "changeLogin";

    /** Логин, введённый на шаге 1 (номер цифрами или почта). Есть заметка — идёт шаг 2. */
    static final String NOTE_LOGIN = "platform.login.login";

    static final String STEP_LOGIN = "login";
    static final String STEP_PASSWORD = "password";
    static final String STEP_METHODS = "methods";

    private static final String PHONE_LOGIN = "\\d{10,15}";

    /** Вход текущего запроса: формы ошибок встроенного класса строятся без него (createLoginForm). */
    private AuthenticationFlowContext current;

    public PasswordLoginForm(KeycloakSession session) {
        super(session);
    }

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        current = context;
        if (context.getUser() != null && !alreadyAuthenticatedUsingPasswordlessCredential(context)) {
            // Повторный вход (пользователь уже известен): шаг 1 не нужен. Пароль выключен и способ один — сразу он.
            context.getAuthenticationSession().setAuthNote(USER_SET_BEFORE_USERNAME_PASSWORD_AUTH, "true");
            Step step = step(context);
            Optional<String> auto = step.view().autoStart();
            if (auto.isPresent() && !LoginSteps.PASSKEY.equals(auto.get())) {
                startCode(context, accountTarget(step.user(), auto.get()), true);
                return;
            }
        }
        super.authenticate(context);
    }

    @Override
    public void action(AuthenticationFlowContext context) {
        current = context;
        MultivaluedMap<String, String> formData = context.getHttpRequest().getDecodedFormParameters();
        if (formData.containsKey(BUTTON_CHANGE_LOGIN)) {
            changeLogin(context);
            return;
        }
        if (formData.containsKey(BUTTON_CODE_LOGIN)) {
            requestCode(context, formData);
            return;
        }
        CodeLoginNotes.clear(context.getAuthenticationSession());
        boolean firstStep = login(context) == null && !isUserAlreadySetBeforeUsernamePasswordAuth(context);
        boolean passwordOrPasskey = formData.containsKey(CredentialRepresentation.PASSWORD)
            || formData.containsKey(WebAuthnConstants.AUTHENTICATOR_DATA) || formData.containsKey(WebAuthnConstants.ERROR)
            || formData.containsKey("cancel");
        if (firstStep && !passwordOrPasskey) {
            identify(context, formData);
            return;
        }
        super.action(context);
    }

    // Шаг 1 → 2: логин запомнили, дальше — экран способов (или сразу единственный способ).
    private void identify(AuthenticationFlowContext context, MultivaluedMap<String, String> formData) {
        String typed = formData.getFirst(AuthenticationManager.FORM_USERNAME);
        if (typed == null || typed.isBlank()) {
            context.getEvent().error(Errors.USERNAME_MISSING);
            context.challenge(challenge(context, "platformLoginRequired", Validation.FIELD_USERNAME));
            return;
        }
        String login = PhoneNormalizer.normalizeLogin(typed);
        String off = loginOff(context, login);
        if (off != null) {
            context.getEvent().detail(Details.USERNAME, login).detail(SignInMethods.EVENT_REASON, "login_off_in_project");
            context.getEvent().error(Errors.INVALID_USER);
            context.challenge(challenge(context, off, Validation.FIELD_USERNAME));
            return;
        }
        context.getAuthenticationSession().setAuthNote(NOTE_LOGIN, login);
        context.getAuthenticationSession().setAuthNote(ATTEMPTED_USERNAME, login);

        Step step = step(context);
        Optional<String> auto = step.view().autoStart();
        if (auto.isPresent() && !LoginSteps.PASSKEY.equals(auto.get())) {
            if (step.user() == null) {
                // Незнакомый логин, пароль выключен в проекте: код на то, что ввели.
                startCode(context, CodeTarget.parse(login).orElseThrow(), false);
            } else {
                startCode(context, accountTarget(step.user(), auto.get()), true);
            }
            return;
        }
        MultivaluedMap<String, String> rememberMe = new MultivaluedHashMap<>();
        if (formData.containsKey("rememberMe")) {
            rememberMe.add("rememberMe", formData.getFirst("rememberMe"));
        }
        context.challenge(challenge(context, rememberMe));
    }

    private void changeLogin(AuthenticationFlowContext context) {
        context.getAuthenticationSession().removeAuthNote(NOTE_LOGIN);
        CodeLoginNotes.clear(context.getAuthenticationSession());
        if (!isUserAlreadySetBeforeUsernamePasswordAuth(context)) {
            context.clearUser();
        }
        context.challenge(challenge(context, new MultivaluedHashMap<>()));
    }

    private void requestCode(AuthenticationFlowContext context, MultivaluedMap<String, String> formData) {
        boolean preset = isUserAlreadySetBeforeUsernamePasswordAuth(context);
        if (login(context) == null && !preset) {
            // Запрос в один шаг: код на то, что ввели в поле.
            Optional<CodeTarget> target = CodeTarget.parse(formData.getFirst(AuthenticationManager.FORM_USERNAME));
            if (target.isEmpty()) {
                context.getEvent().error(Errors.USERNAME_MISSING);
                context.failureChallenge(AuthenticationFlowError.INVALID_USER,
                    challenge(context, "platformCodeLoginTargetRequired", Validation.FIELD_USERNAME));
                return;
            }
            String off = loginOff(context, target.get().value());
            if (off != null) {
                context.getEvent().error(Errors.INVALID_USER);
                context.failureChallenge(AuthenticationFlowError.INVALID_USER, challenge(context, off, Validation.FIELD_USERNAME));
                return;
            }
            startCode(context, target.get(), false);
            return;
        }

        Step step = step(context);
        String chosen = formData.getFirst(BUTTON_CODE_LOGIN);
        Optional<CodeTarget> typed = preset ? Optional.empty() : CodeTarget.parse(login(context));
        String method = SignInMethods.ALL.contains(chosen) ? chosen
            : typed.map(target -> LoginSteps.codeMethod(target.channel())).orElse(null);
        if (method == null || !step.view().methods().contains(method)) {
            context.challenge(challenge(context, "platformLoginMethodUnavailable", null));
            return;
        }
        if (step.view().password() || step.user() == null) {
            // Код на то, что ввели: на экране — как ввели, есть аккаунт или нет.
            startCode(context, typed.orElseThrow(), false);
        } else {
            startCode(context, accountTarget(step.user(), method), true);
        }
    }

    // Есть ли такой пользователь, здесь не проверяем и не показываем: экран кода — одинаковый для всех.
    private void startCode(AuthenticationFlowContext context, CodeTarget target, boolean masked) {
        if (!isUserAlreadySetBeforeUsernamePasswordAuth(context)) {
            context.clearUser();
        }
        context.getEvent().detail(Details.USERNAME, target.value());
        context.getAuthenticationSession().setAuthNote(ATTEMPTED_USERNAME, target.value());
        CodeLoginNotes.save(context.getAuthenticationSession(), target, masked);
        context.success();
    }

    // Код на почту или номер из аккаунта (шаг «способы» есть только у существующего пользователя).
    private static CodeTarget accountTarget(UserModel user, String method) {
        return SignInMethods.EMAIL_CODE.equals(method)
            ? new CodeTarget(CodeTarget.Channel.EMAIL, user.getEmail().trim().toLowerCase(Locale.ROOT))
            : new CodeTarget(CodeTarget.Channel.SMS, user.getUsername());
    }

    /**
     * Шаг 2 для текущего входа.
     *
     * @param user пользователь (уже известный или найденный по логину); null — нет такого
     * @param view что показать
     */
    private record Step(UserModel user, LoginSteps.View view) {
    }

    private Step step(AuthenticationFlowContext context) {
        boolean preset = isUserAlreadySetBeforeUsernamePasswordAuth(context);
        String login = login(context);
        UserModel user = preset ? context.getUser() : findUser(context, login);
        Optional<CodeTarget.Channel> typed = preset ? Optional.empty() : CodeTarget.parse(login).map(CodeTarget::channel);
        LoginSteps.Account account = null;
        if (user != null) {
            SignInMethods.Facts facts = SignInMethods.facts(context.getSession(), context.getRealm(), user);
            Set<String> disabled = new HashSet<>(SignInMethods.effectiveDisabled(facts));
            for (String method : SignInMethods.ALL) {
                if (SignInMethods.offInProject(context.getSession(), context.getRealm(), user, method)) {
                    disabled.add(method);
                }
            }
            account = new LoginSteps.Account(user.isEnabled(), disabled, facts.hasPasskey(),
                facts.hasEmail(), user.getUsername().matches(PHONE_LOGIN));
        }
        boolean passwordOn = ProjectSignInRealm.on(context.getSession(), context.getRealm(), ProjectSignIn.Method.PASSWORD);
        return new Step(user, LoginSteps.view(typed, account, webauthnAuth != null && webauthnAuth.isPasskeysEnabled(), passwordOn));
    }

    private static UserModel findUser(AuthenticationFlowContext context, String login) {
        try {
            return KeycloakModelUtils.findUserByNameOrEmail(context.getSession(), context.getRealm(), login);
        } catch (ModelDuplicateException e) {
            return null;
        }
    }

    /**
     * Вход по номеру или по почте выключен в проекте (ProjectSignIn): ключ сообщения или null. Незнакомый логин —
     * если способ не включён совсем (регистрации им нет); знакомый — если у человека нет права переходного периода.
     */
    private static String loginOff(AuthenticationFlowContext context, String login) {
        Optional<CodeTarget> typed = CodeTarget.parse(login);
        if (typed.isEmpty()) {
            return null;
        }
        ProjectSignIn.Method method = ProjectSignInRealm.method(typed.get().channel());
        UserModel user = findUser(context, typed.get().value());
        boolean off = user == null
            ? !ProjectSignInRealm.on(context.getSession(), context.getRealm(), method)
            : ProjectSignInRealm.blocked(context.getSession(), context.getRealm(), user).contains(method);
        if (!off) {
            return null;
        }
        return method == ProjectSignIn.Method.PHONE ? "platformLoginPhoneOff" : "platformLoginEmailOff";
    }

    private static String login(AuthenticationFlowContext context) {
        return context.getAuthenticationSession().getAuthNote(NOTE_LOGIN);
    }

    // Атрибуты шага для страницы входа — на всех её показах, в том числе с ошибкой.
    private void decorate(AuthenticationFlowContext context, LoginFormsProvider form) {
        boolean preset = isUserAlreadySetBeforeUsernamePasswordAuth(context);
        String login = login(context);
        if (!preset && login == null) {
            form.setAttribute("loginStep", STEP_LOGIN);
            return;
        }
        Step step = step(context);
        LoginSteps.View view = step.view();
        form.setAttribute("loginStep", view.password() ? STEP_PASSWORD : STEP_METHODS);
        form.setAttribute("loginMethods", String.join(",", view.methods()));
        if (!preset) {
            form.setAttribute("loginDisplay", CodeTarget.parse(login).map(CodeTarget::display).orElse(login));
        }
        if (!view.password()) {
            UserModel user = step.user();
            if (view.methods().contains(SignInMethods.EMAIL_CODE)) {
                form.setAttribute("loginEmailHint", accountTarget(user, SignInMethods.EMAIL_CODE).masked());
            }
            if (view.methods().contains(SignInMethods.PHONE_CODE)) {
                form.setAttribute("loginPhoneHint", accountTarget(user, SignInMethods.PHONE_CODE).masked());
            }
            view.autoStart().ifPresent(method -> form.setAttribute("loginAutoStart", method));
        }
        if (view.methods().contains(LoginSteps.PASSKEY)) {
            webauthnAuth.fillContextForm(context);
        }
    }

    @Override
    protected Response challenge(AuthenticationFlowContext context, MultivaluedMap<String, String> formData) {
        decorate(context, context.form());
        return super.challenge(context, formData);
    }

    @Override
    protected Response createLoginForm(LoginFormsProvider form) {
        if (current != null) {
            decorate(current, form);
        }
        return super.createLoginForm(form);
    }

    @Override
    public boolean validatePassword(AuthenticationFlowContext context, UserModel user,
                                    MultivaluedMap<String, String> inputData, boolean clearUser) {
        return !SignInMethods.rejectPasswordOff(context, user,
                () -> challenge(context, "platformPasswordOff", Validation.FIELD_PASSWORD), clearUser)
            && super.validatePassword(context, user, inputData, clearUser)
            && !SignInMethods.rejectPassword(context, user,
                () -> challenge(context, getDefaultChallengeMessage(context), Validation.FIELD_PASSWORD), clearUser);
    }

    @Override
    protected boolean validateForm(AuthenticationFlowContext context, MultivaluedMap<String, String> formData) {
        MultivaluedMap<String, String> data = withLogin(context, formData);
        String username = data.getFirst(AuthenticationManager.FORM_USERNAME);
        String off = username == null || isUserAlreadySetBeforeUsernamePasswordAuth(context) ? null : loginOff(context, username);
        if (off != null) {
            context.getEvent().error(Errors.INVALID_USER);
            context.failureChallenge(AuthenticationFlowError.INVALID_USER, challenge(context, off, Validation.FIELD_USERNAME));
            return false;
        }
        return validateUserAndPassword(context, data);
    }

    // Копия данных формы с логином шага 1 (или приведённым введённым — запрос в один шаг); остальные поля — как пришли.
    private static MultivaluedMap<String, String> withLogin(AuthenticationFlowContext context,
                                                            MultivaluedMap<String, String> formData) {
        MultivaluedMap<String, String> copy = new MultivaluedHashMap<>(formData);
        String login = login(context);
        String username = login != null ? login : formData.getFirst(AuthenticationManager.FORM_USERNAME);
        if (username != null) {
            copy.putSingle(AuthenticationManager.FORM_USERNAME, PhoneNormalizer.normalizeLogin(username));
        }
        return copy;
    }
}
