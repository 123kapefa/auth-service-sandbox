package sandbox.keycloak.codelogin;

import java.security.SecureRandom;

import org.keycloak.credential.hash.PasswordHashProvider;
import org.keycloak.models.PasswordPolicy;
import org.keycloak.models.credential.PasswordCredentialModel;

/**
 * Алгоритм хэша «bcrypt» для паролей, перенесённых из боевого сервиса (credentialData.algorithm = "bcrypt").
 * Нужен только для проверки: политика паролей realm на bcrypt не переключается, поэтому после верного входа Keycloak
 * сам заменяет хэш на свой (алгоритм политики) — перенесённые BCrypt-хэши постепенно исчезают.
 */
public final class BcryptPasswordHashProvider implements PasswordHashProvider {

    public static final String ID = "bcrypt";
    /** Как в боевом сервисе (BcryptPasswordService.WorkFactor). */
    private static final int DEFAULT_COST = 12;

    private final SecureRandom random = new SecureRandom();

    @Override
    public boolean policyCheck(PasswordPolicy policy, PasswordCredentialModel credential) {
        int cost = policy.getHashIterations() > 0 ? policy.getHashIterations() : DEFAULT_COST;
        return ID.equals(credential.getPasswordCredentialData().getAlgorithm())
            && credential.getPasswordCredentialData().getHashIterations() == cost;
    }

    @Override
    public PasswordCredentialModel encodedCredential(String rawPassword, int iterations) {
        int cost = iterations > 0 ? iterations : DEFAULT_COST;
        // Соль — внутри самого хэша BCrypt; отдельное поле salt Keycloak не нужно.
        return PasswordCredentialModel.createFromValues(ID, new byte[0], cost, BcryptHash.hash(rawPassword, cost, random));
    }

    @Override
    public boolean verify(String rawPassword, PasswordCredentialModel credential) {
        return BcryptHash.matches(rawPassword, credential.getPasswordSecretData().getValue());
    }

    @Override
    public void close() {
    }
}
