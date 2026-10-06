package platform.keycloak.auth;

import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.Set;

/**
 * Вход в два шага (10.4): шаг 1 — телефон или почта, шаг 2 — чем войти. Здесь — что показать на шаге 2 (без Keycloak).
 *
 * <ul>
 *   <li><b>Пароль</b> — поле пароля сразу, ниже ключ доступа и код на то, что ввели (номер — звонок, почта — письмо).
 *       Так выглядит шаг 2 для всех, у кого вход по паролю не выключен, и для незнакомого логина: список зависит только
 *       от того, что ввели, — по экрану не понять, есть ли аккаунт и что у него настроено.</li>
 *   <li><b>Способы</b> — человек сам выключил пароль (10.3): включённые у него способы по цене — ключ доступа → код на
 *       почту → звонок. Код уходит на почту и номер из аккаунта, на экране они скрыты частично. Один способ — шаг 2
 *       пропускается.</li>
 * </ul>
 */
final class LoginSteps {

    static final String PASSKEY = "passkey";

    private LoginSteps() {
    }

    /**
     * Что известно об аккаунте.
     *
     * @param enabled    учётная запись включена
     * @param disabled   выключенные способы — с учётом страховки (SignInMethods.effectiveDisabled)
     * @param hasPasskey есть ключ доступа
     * @param hasEmail   есть почта
     * @param phoneLogin логин — номер телефона (на него и уходит код)
     */
    record Account(boolean enabled, Set<String> disabled, boolean hasPasskey, boolean hasEmail, boolean phoneLogin) {
    }

    /**
     * Шаг 2.
     *
     * @param password поле пароля сразу (иначе — список способов)
     * @param methods  способы по цене: passkey, email-code, phone-code
     */
    record View(boolean password, List<String> methods) {

        /** Единственный способ, который открывается сам, без шага 2. */
        Optional<String> autoStart() {
            return !password && methods.size() == 1 ? Optional.of(methods.get(0)) : Optional.empty();
        }
    }

    /**
     * @param typed            что ввели на шаге 1 (номер, почта; пусто — логин не номер и не почта или его не вводили)
     * @param account          аккаунт с таким логином; null — нет такого
     * @param passkeysEnabled  ключи доступа включены в realm
     */
    static View view(Optional<CodeTarget.Channel> typed, Account account, boolean passkeysEnabled) {
        List<String> methods = new ArrayList<>();
        if (account == null || !account.enabled() || !account.disabled().contains(SignInMethods.PASSWORD)) {
            if (passkeysEnabled) {
                methods.add(PASSKEY);
            }
            typed.map(LoginSteps::codeMethod).ifPresent(methods::add);
            return new View(true, List.copyOf(methods));
        }
        if (passkeysEnabled && account.hasPasskey()) {
            methods.add(PASSKEY);
        }
        if (account.hasEmail() && !account.disabled().contains(SignInMethods.EMAIL_CODE)) {
            methods.add(SignInMethods.EMAIL_CODE);
        }
        if (account.phoneLogin() && !account.disabled().contains(SignInMethods.PHONE_CODE)) {
            methods.add(SignInMethods.PHONE_CODE);
        }
        return new View(false, List.copyOf(methods));
    }

    static String codeMethod(CodeTarget.Channel channel) {
        return channel == CodeTarget.Channel.SMS ? SignInMethods.PHONE_CODE : SignInMethods.EMAIL_CODE;
    }
}
