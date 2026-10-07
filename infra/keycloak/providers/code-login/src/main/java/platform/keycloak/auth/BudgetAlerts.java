package platform.keycloak.auth;

import java.io.IOException;
import java.math.BigDecimal;
import java.text.MessageFormat;
import java.text.NumberFormat;
import java.time.ZoneId;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Objects;
import java.util.Properties;
import java.util.stream.Stream;

import org.jboss.logging.Logger;
import org.keycloak.email.EmailException;
import org.keycloak.email.EmailTemplateProvider;
import org.keycloak.models.ClientModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.RoleModel;
import org.keycloak.models.UserModel;

/**
 * Письмо админам проекта о расходе на коды: порог из настроек (80 %) и исчерпанный бюджет (100 %). Админ проекта — у
 * кого есть право sign-in.manage в клиенте сервиса входа (auth-api; в песочнице sandbox-api): ролью, через составную
 * роль или группу. Письмо — каждому на его языке, в оформлении проекта; без почты, выключенным и служебным учёткам —
 * не пишем. Не чаще раза в сутки на порог — следит CodeLimitStore (отметка рядом с расходом).
 */
final class BudgetAlerts {

    static final String PERMISSION = "sign-in.manage";
    static final List<String> API_CLIENTS = List.of("auth-api", "sandbox-api");
    /** Больше писем за раз не шлём: отправка — в запросе кода, письма уходят по одному. */
    static final int MAX_RECIPIENTS = 50;
    static final String TEMPLATE = "platform-code-budget.ftl";

    private static final Logger LOG = Logger.getLogger(BudgetAlerts.class);

    private BudgetAlerts() {
    }

    /**
     * Разослать письмо о пороге; возвращает, скольким ушло. Ошибки почты — в лог: отправку кода они не ломают.
     *
     * @param spent  расход за сутки, копейки
     * @param budget дневной бюджет, копейки
     */
    static int send(KeycloakSession session, RealmModel realm, int percent, long spent, long budget, ZoneId zone) {
        List<UserModel> recipients = recipients(session, realm);
        if (recipients.isEmpty()) {
            LOG.warnf("[CODE BUDGET] %s: письмо о %d %% бюджета некому отправить — нет админов с почтой", realm.getName(), percent);
            return 0;
        }
        String project = AccountNotices.realmName(realm);
        int sent = 0;
        for (UserModel user : recipients) {
            try {
                Locale locale = session.getContext().resolveLocale(user);
                Properties messages = AccountNotices.messages(session, realm, user);
                Map<String, Object> attributes = new HashMap<>(texts(messages, locale, project, percent, spent, budget, zone));
                String subject = percent >= 100 ? "platformCodeBudgetExhaustedEmailSubject" : "platformCodeBudgetAlertEmailSubject";
                session.getProvider(EmailTemplateProvider.class).setRealm(realm).setUser(user)
                    .send(subject, List.of(percent), TEMPLATE, attributes);
                sent++;
            } catch (EmailException | IOException | RuntimeException e) {
                LOG.warnf(e, "[CODE BUDGET] %s: письмо о %d %% бюджета админу %s не ушло", realm.getName(), percent, user.getUsername());
            }
        }
        LOG.infof("[CODE BUDGET] %s: письмо о %d %% бюджета — админам: %d", realm.getName(), percent, sent);
        return sent;
    }

    /**
     * Тексты письма (атрибуты шаблона): budgetTitle, budgetText, budgetEffect, budgetNote. Суммы — в рублях, по языку
     * письма.
     */
    static Map<String, String> texts(Properties messages, Locale locale, String project, int percent, long spent,
                                     long budget, ZoneId zone) {
        boolean exhausted = percent >= 100;
        Object[] args = {project, rubles(spent, locale), rubles(budget, locale), percent, zone.getId()};
        return Map.of(
            "budgetTitle", format(messages, exhausted ? "platformCodeBudgetExhaustedTitle" : "platformCodeBudgetAlertTitle", locale, args),
            "budgetText", format(messages, exhausted ? "platformCodeBudgetExhaustedText" : "platformCodeBudgetAlertText", locale, args),
            "budgetEffect", format(messages, exhausted ? "platformCodeBudgetExhaustedEffect" : "platformCodeBudgetAlertEffect", locale, args),
            "budgetNote", format(messages, "platformCodeBudgetNote", locale, args));
    }

    /** Копейки → рубли по языку письма: целые — без копеек («1 000»), иначе с двумя знаками («10,50»). */
    static String rubles(long kopecks, Locale locale) {
        NumberFormat format = NumberFormat.getNumberInstance(locale);
        format.setMinimumFractionDigits(kopecks % 100 == 0 ? 0 : 2);
        format.setMaximumFractionDigits(2);
        return format.format(BigDecimal.valueOf(kopecks, 2));
    }

    /**
     * Кому писать: включённые люди с почтой и правом sign-in.manage — прямо ролью, составной ролью (admin) или
     * группой. Без клиента сервиса входа или права в нём — никому.
     */
    static List<UserModel> recipients(KeycloakSession session, RealmModel realm) {
        RoleModel permission = API_CLIENTS.stream()
            .map(realm::getClientByClientId)
            .filter(Objects::nonNull)
            .map(client -> client.getRole(PERMISSION))
            .filter(Objects::nonNull)
            .findFirst()
            .orElse(null);
        if (permission == null) {
            return List.of();
        }
        Stream<UserModel> byRole = Stream.concat(realm.getRolesStream(), realm.getClientsStream().flatMap(ClientModel::getRolesStream))
            .filter(role -> role.hasRole(permission))
            .flatMap(role -> session.users().getRoleMembersStream(realm, role));
        Stream<UserModel> byGroup = realm.getGroupsStream()
            .filter(group -> group.hasRole(permission))
            .flatMap(group -> session.users().getGroupMembersStream(realm, group));
        Map<String, UserModel> users = new LinkedHashMap<>();
        Stream.concat(byRole, byGroup)
            .filter(user -> user.isEnabled() && user.getServiceAccountClientLink() == null)
            .filter(user -> user.getEmail() != null && !user.getEmail().isBlank())
            .forEach(user -> users.putIfAbsent(user.getId(), user));
        return users.values().stream().limit(MAX_RECIPIENTS).toList();
    }

    private static String format(Properties messages, String key, Locale locale, Object[] args) {
        return new MessageFormat(messages.getProperty(key, key), locale).format(args);
    }
}
