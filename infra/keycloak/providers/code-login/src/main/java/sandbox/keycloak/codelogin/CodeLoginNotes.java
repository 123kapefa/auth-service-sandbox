package sandbox.keycloak.codelogin;

import java.util.Optional;

import org.keycloak.sessions.AuthenticationSessionModel;

/** Заметки сессии входа (auth notes): живут на сервере, пока идёт вход; браузер их не видит и подменить не может. */
final class CodeLoginNotes {

    /** Кому отправлять код: номер (79990000001) или почта. Есть заметка — значит, нажали «Войти по коду». */
    static final String TARGET = "sandbox.code-login.target";
    /** Канал: SMS или EMAIL. */
    static final String CHANNEL = "sandbox.code-login.channel";

    private CodeLoginNotes() {
    }

    static void save(AuthenticationSessionModel session, CodeTarget target) {
        session.setAuthNote(TARGET, target.value());
        session.setAuthNote(CHANNEL, target.channel().name());
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
    }
}
