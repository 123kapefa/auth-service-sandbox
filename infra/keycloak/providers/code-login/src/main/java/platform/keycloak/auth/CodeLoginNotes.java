package platform.keycloak.auth;

import java.util.Optional;

import org.keycloak.sessions.AuthenticationSessionModel;

/** Заметки сессии входа (auth notes): живут на сервере, пока идёт вход; браузер их не видит и подменить не может. */
final class CodeLoginNotes {

    /** Кому отправлять код: номер (79990000001) или почта. Есть заметка — значит, нажали «Войти по коду». */
    static final String TARGET = "platform.code-login.target";
    /** Канал: SMS или EMAIL. */
    static final String CHANNEL = "platform.code-login.channel";
    /** "true" — адрес взят из аккаунта (вход в два шага), на экране кода он скрыт частично. */
    static final String MASKED = "platform.code-login.masked";

    private CodeLoginNotes() {
    }

    static void save(AuthenticationSessionModel session, CodeTarget target) {
        save(session, target, false);
    }

    static void save(AuthenticationSessionModel session, CodeTarget target, boolean masked) {
        session.setAuthNote(TARGET, target.value());
        session.setAuthNote(CHANNEL, target.channel().name());
        if (masked) {
            session.setAuthNote(MASKED, "true");
        } else {
            session.removeAuthNote(MASKED);
        }
    }

    static boolean masked(AuthenticationSessionModel session) {
        return Boolean.parseBoolean(session.getAuthNote(MASKED));
    }

    static Optional<CodeTarget> load(AuthenticationSessionModel session) {
        String value = session.getAuthNote(TARGET);
        String channel = session.getAuthNote(CHANNEL);
        return value == null || channel == null
            ? Optional.empty()
            : Optional.of(new CodeTarget(CodeTarget.Channel.valueOf(channel), value));
    }

    static void clear(AuthenticationSessionModel session) {
        session.removeAuthNote(TARGET);
        session.removeAuthNote(CHANNEL);
        session.removeAuthNote(MASKED);
    }
}
