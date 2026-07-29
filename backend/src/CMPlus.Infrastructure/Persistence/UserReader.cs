using CMPlus.Application.Abstractions;
using CMPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>
/// <see cref="IUserReader"/> implementation. Explicitly bypasses the tenant global query filter
/// (<see cref="IgnoreQueryFilters"/>) because login happens before any tenant is known - this is
/// the single, narrowly-scoped, documented escape hatch ADR-0002 anticipates ("admin cross-tenant
/// tooling needs explicit filter bypass with audit"); it returns only the fields
/// <see cref="UserAuthRecord"/> needs, never the full <c>User</c> entity, and is used from nowhere
/// except <c>LoginCommandHandler</c>.
/// </summary>
public sealed class UserReader(CmPlusDbContext dbContext) : IUserReader
{
    public async Task<UserAuthRecord?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.Email == email)
            .Select(u => new UserAuthRecord(u.Id, u.TenantId, u.Email, u.Role, u.PasswordHash))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
