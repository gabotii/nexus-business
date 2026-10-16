using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nexus.Application.Abstractions;

namespace Nexus.Application.Common;

/// <summary>One page of a list plus the total, so the client can render "1–25 de 312" and the page links.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public static class Paging
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static async Task<PagedResult<T>> ToPageAsync<T>(this IQueryable<T> query, int page, int pageSize,
        CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<T>(items, total, page, pageSize);
    }

    /// <summary>
    /// A LIKE pattern matching <paramref name="term"/> anywhere, with the wildcard characters of the term escaped
    /// (use with <c>EF.Functions.Like(column, pattern, "\\")</c>).
    /// </summary>
    public static string ContainsPattern(string term) =>
        "%" + term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";

    public static IRuleBuilderOptions<T, int> ValidPage<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(1, 100_000).WithMessage("La página no es válida.");

    public static IRuleBuilderOptions<T, int> ValidPageSize<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(1, MaxPageSize).WithMessage($"El tamaño de página debe estar entre 1 y {MaxPageSize}.");
}

public static class Concurrency
{
    /// <summary>
    /// Saves <paramref name="entity"/> only if it still has the <paramref name="expectedVersion"/> the client read
    /// (PostgreSQL <c>xmin</c>); otherwise someone else changed it meanwhile and the client must reload.
    /// </summary>
    public static async Task SaveExpectingAsync<TEntity>(this IAppDbContext db, TEntity entity, uint expectedVersion,
        CancellationToken ct) where TEntity : class
    {
        db.Entry(entity).Property("Version").OriginalValue = expectedVersion;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("concurrency_conflict",
                "Otra persona ha modificado este registro mientras lo editabas. Recarga para ver los cambios.");
        }
    }
}
