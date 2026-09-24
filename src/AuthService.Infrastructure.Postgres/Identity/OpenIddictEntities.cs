using OpenIddict.EntityFrameworkCore.Models;

namespace AuthService.Infrastructure.Postgres.Identity;

public sealed class OidcApplication : OpenIddictEntityFrameworkCoreApplication<Guid, OidcAuthorization, OidcToken>;

public sealed class OidcAuthorization : OpenIddictEntityFrameworkCoreAuthorization<Guid, OidcApplication, OidcToken>;

public sealed class OidcScope : OpenIddictEntityFrameworkCoreScope<Guid>;

public sealed class OidcToken : OpenIddictEntityFrameworkCoreToken<Guid, OidcApplication, OidcAuthorization>;
