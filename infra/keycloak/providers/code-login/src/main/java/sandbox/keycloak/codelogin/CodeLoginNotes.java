package sandbox.keycloak.codelogin;

/** Заметки сессии входа (auth notes): живут на сервере, пока идёт вход; браузер их не видит и подменить не может. */
final class CodeLoginNotes {

    /** Кому отправлять код: номер в виде логина (79990000001). Есть заметка — значит, нажали «Войти по коду». */
    static final String TARGET = "sandbox.code-login.target";

    private CodeLoginNotes() {
    }
}
