using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nexus.Application.Abstractions;
using Nexus.Infrastructure.Persistence;

namespace Nexus.Infrastructure.Identity;

internal sealed class UserDirectory(AppDbContext db, ILookupNormalizer normalizer) : IUserDirectory
{
    public Task<UserSummary?> FindAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserSummary(u.Id, u.Email!, u.DisplayName, u.EmailConfirmed))
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, UserSummary>> GetManyAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new UserSummary(u.Id, u.Email!, u.DisplayName, u.EmailConfirmed))
            .ToDictionaryAsync(u => u.Id, ct);
    }

    public async Task<Guid?> FindIdByEmailAsync(string email, CancellationToken ct)
    {
        var normalized = NormalizeEmail(email);
        var id = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalized)
            .Select(u => u.Id)
            .SingleOrDefaultAsync(ct);
        return id == Guid.Empty ? null : id;
    }

    public string NormalizeEmail(string email) => normalizer.NormalizeEmail(email.Trim());
}
