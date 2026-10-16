using System.Reflection;
using Nexus.Application;
using Nexus.Domain.Common;
using Nexus.Infrastructure.Persistence;

namespace Nexus.UnitTests;

/// <summary>Layer rules from ADR-0001, checked on the compiled assemblies.</summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(DomainException).Assembly;
    private static readonly Assembly Application = typeof(DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(AppDbContext).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_but_the_base_library() =>
        AssertNoReference(Domain, "Nexus.", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "FluentValidation");

    [Fact]
    public void Application_knows_neither_http_nor_the_database_provider() =>
        AssertNoReference(Application, "Nexus.Infrastructure", "Nexus.Api", "Microsoft.AspNetCore", "Npgsql");

    [Fact]
    public void Infrastructure_does_not_depend_on_the_api() =>
        AssertNoReference(Infrastructure, "Nexus.Api");

    /// <summary>
    /// ADR-0004: bypassing the tenant query filter is allowed only where a read legitimately spans companies. Adding a
    /// new call means adding it here, in review, with the reason.
    /// </summary>
    [Fact]
    public void Tenant_filter_is_bypassed_only_in_the_allow_list()
    {
        var allowed = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // "My companies": memberships and roles of the caller across tenants (RLS still limits them to the caller).
            ["Nexus.Application/Tenancy/TenantService.cs"] = 2,
        };
        var src = Path.Combine(RepositoryRoot(), "backend", "src");

        var actual = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (File: Path.GetRelativePath(src, f).Replace('\\', '/'),
                Count: CountOccurrences(File.ReadAllText(f), "IgnoreQueryFilters(")))
            .Where(x => x.Count > 0)
            .ToDictionary(x => x.File, x => x.Count, StringComparer.Ordinal);

        Assert.Equal(allowed.OrderBy(x => x.Key), actual.OrderBy(x => x.Key));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "backend", "Nexus.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static void AssertNoReference(Assembly assembly, params string[] forbiddenPrefixes)
    {
        var offending = assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => forbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

        Assert.True(offending.Count == 0,
            $"{assembly.GetName().Name} must not reference: {string.Join(", ", offending)}");
    }
}
