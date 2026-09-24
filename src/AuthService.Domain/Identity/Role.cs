using Microsoft.AspNetCore.Identity;

namespace AuthService.Domain.Identity;

public sealed class Role : IdentityRole<Guid>
{
    public Role()
    {
    }

    public Role(string name)
        : base(name)
    {
        Id = Guid.CreateVersion7();
    }
}
