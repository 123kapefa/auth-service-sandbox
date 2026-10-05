using System.Text.RegularExpressions;
using AuthService.Contracts.Services;

namespace AuthService.Client.AppCatalog;

/// <summary>
/// Каталог приложения в коде: какие права проверяет код и с какими ролями приложение работает «из коробки».
/// <para>
/// <b>Зачем.</b> Права проверяет код — <c>RequirePermissions("orders.view")</c>. Если заводить их руками в админке,
/// право легко забыть или опечататься в имени (запросы молча получают 403), а админ может удалить право, которое код
/// ещё проверяет. Объявленное здесь право сервис входа заводит сам при старте сервиса, помечает «из кода» и не даёт
/// удалить в админке. Роли — стартовый набор: с ними приложение работает сразу после установки, без ручной настройки.
/// </para>
/// <para>
/// <b>Что остаётся админу.</b> Состав ролей после создания: роль заводится один раз, дальше её права меняет админ
/// («Роли и права» → приложение), и следующий старт сервиса его правки не затрёт. Свои роли и права — тоже в админке.
/// Кому какую роль выдать — в админке («Пользователи»); роль по умолчанию (у всех) задаёт add-app.py --default-role.
/// </para>
/// <para>
/// <b>Как подключить.</b> Ключу сервиса нужен код приложения и право «объявлять каталог»: в админке → «Сервисы и
/// ключи» → ключ с кодом приложения (orders → service-orders) и правом service.catalog.declare. Затем в Program.cs:
/// <code>
/// builder.Services.AddAppCatalog(builder.Configuration, catalog => catalog
///     .Permission(OrderPermissions.VIEW, "Свои заказы")
///     .Permission(OrderPermissions.CREATE, "Оформить заказ")
///     .Role("customer", "Покупатель", OrderPermissions.VIEW, OrderPermissions.CREATE));
/// </code>
/// Имена прав держите константами и используйте их же в RequirePermissions — так объявление и проверка не разойдутся.
/// </para>
/// </summary>
public sealed partial class AppCatalogBuilder
{
    private readonly List<DeclaredPermission> _permissions = [];
    private readonly List<DeclaredRole> _roles = [];

    /// <summary>
    /// Право, которое проверяет код. Имя — части через точку, строчная латиница и цифры: <c>orders.view</c>,
    /// <c>reports.export</c>. Описание — что даёт право; его видит админ, когда собирает роли.
    /// </summary>
    /// <remarks>
    /// Удалили право из кода — в сервисе входа оно остаётся (может входить в роли) и попадает в лог как «осиротевшее»:
    /// убрать его из ролей и удалить — решение админа.
    /// </remarks>
    public AppCatalogBuilder Permission(string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!PermissionName().IsMatch(name))
        {
            throw new ArgumentException($"Право «{name}»: части через точку, строчная латиница и цифры (orders.view).", nameof(name));
        }

        if (_permissions.Exists(permission => string.Equals(permission.Name, name, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Право «{name}» объявлено дважды.", nameof(name));
        }

        _permissions.Add(new DeclaredPermission(name, description));
        return this;
    }

    /// <summary>
    /// Встроенная роль (она же режим, в котором работает пользователь) и её права при создании. Права — только
    /// объявленные выше через <see cref="Permission"/>: роль из кода не может опираться на право, которое знает
    /// только админка.
    /// </summary>
    /// <remarks>
    /// Роль создаётся, только если её ещё нет, и удалить её в админке нельзя. Есть — не меняется: составом управляет
    /// админ. Поменять состав встроенной роли из кода нельзя намеренно — иначе каждый старт сервиса откатывал бы
    /// решения админа. Пустая роль по умолчанию, которую завёл add-app.py, получает объявленный здесь состав.
    /// </remarks>
    public AppCatalogBuilder Role(string name, string description, params string[] permissions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(permissions);
        if (!RoleName().IsMatch(name))
        {
            throw new ArgumentException($"Роль «{name}»: строчная латиница, цифры и дефис, с буквы (customer).", nameof(name));
        }

        if (_roles.Exists(role => string.Equals(role.Name, name, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Роль «{name}» объявлена дважды.", nameof(name));
        }

        string[] unknown = [.. permissions.Where(permission => !_permissions.Exists(declared => string.Equals(declared.Name, permission, StringComparison.Ordinal)))];
        if (unknown.Length > 0)
        {
            throw new ArgumentException($"Роль «{name}»: права {string.Join(", ", unknown)} не объявлены — сначала Permission(...).", nameof(permissions));
        }

        _roles.Add(new DeclaredRole(name, description, [.. permissions.Distinct(StringComparer.Ordinal)]));
        return this;
    }

    /// <summary>Каталог для сервиса входа (PUT /auth/services/catalog).</summary>
    public DeclareCatalogRequest Build() => new([.. _permissions], [.. _roles]);

    // Те же правила имён, что в админке сервиса входа.
    [GeneratedRegex(@"^[a-z][a-z0-9]*(\.[a-z0-9][a-z0-9_-]*)+$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex PermissionName();

    [GeneratedRegex("^[a-z][a-z0-9-]{1,49}$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex RoleName();
}
