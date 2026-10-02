package sandbox.keycloak.codelogin;

import java.security.SecureRandom;
import java.util.regex.Pattern;

import org.bouncycastle.crypto.generators.OpenBSDBCrypt;

/**
 * BCrypt-хэши паролей боевого сервиса (BCrypt.Net-Next: «$2a$12$…»). Проверка — BouncyCastle, он уже есть в Keycloak.
 * Как и BCrypt.Net, учитываются только первые 72 байта пароля (UTF-8): так устроен сам BCrypt.
 */
public final class BcryptHash {

    // $2a$/$2b$/$2y$, стоимость — две цифры, дальше 22 символа соли и 31 — хэша.
    private static final Pattern FORMAT = Pattern.compile("^\\$2[aby]\\$\\d{2}\\$[./A-Za-z0-9]{53}$");
    private static final int SALT_BYTES = 16;

    private BcryptHash() {
    }

    /** Пароль подходит к хэшу. Не BCrypt-хэш (пусто, обрезан, чужой формат) — не подходит, без исключений. */
    public static boolean matches(String rawPassword, String hash) {
        if (rawPassword == null || hash == null || !FORMAT.matcher(hash).matches()) {
            return false;
        }
        return OpenBSDBCrypt.checkPassword(hash, rawPassword.toCharArray());
    }

    /** Стоимость (work factor) из хэша: «$2a$12$…» → 12. */
    public static int cost(String hash) {
        if (hash == null || !FORMAT.matcher(hash).matches()) {
            throw new IllegalArgumentException("Не BCrypt-хэш");
        }
        return Integer.parseInt(hash.substring(4, 6));
    }

    /** Новый хэш «$2a$…» — тот же формат, что у BCrypt.Net. */
    public static String hash(String rawPassword, int cost, SecureRandom random) {
        byte[] salt = new byte[SALT_BYTES];
        random.nextBytes(salt);
        return OpenBSDBCrypt.generate("2a", rawPassword.toCharArray(), salt, cost);
    }
}
