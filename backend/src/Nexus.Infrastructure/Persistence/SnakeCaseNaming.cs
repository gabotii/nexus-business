using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Nexus.Infrastructure.Persistence;

/// <summary>
/// Maps tables, columns, keys, foreign keys and indexes to PostgreSQL-style snake_case names, so hand-written SQL
/// (reports, Row-Level Security policies) never needs quoted identifiers. Applied after all configurations; names
/// that are already snake_case are left as they are.
/// </summary>
internal static class SnakeCaseNaming
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is not null)
            {
                entity.SetTableName(ToSnakeCase(table));
            }

            var tableId = table is null ? (StoreObjectIdentifier?)null
                : StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());

            foreach (var property in entity.GetProperties())
            {
                if (IsXminRowVersion(property))
                {
                    // Npgsql maps uint row versions to the xmin system column by convention; an explicit name
                    // would turn them into a real (always null) column instead.
                    continue;
                }

                var column = tableId is { } id ? property.GetColumnName(id) : property.GetColumnName();
                if (column is not null)
                {
                    property.SetColumnName(ToSnakeCase(column));
                }
            }

            foreach (var key in entity.GetKeys())
            {
                if (key.GetName() is { } name)
                {
                    key.SetName(ToSnakeCase(name));
                }
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                if (foreignKey.GetConstraintName() is { } name)
                {
                    foreignKey.SetConstraintName(ToSnakeCase(name));
                }
            }

            foreach (var index in entity.GetIndexes())
            {
                if (index.GetDatabaseName() is { } name)
                {
                    index.SetDatabaseName(ToSnakeCase(name));
                }
            }
        }
    }

    private static bool IsXminRowVersion(IMutableProperty property) =>
        property.ClrType == typeof(uint) && property.IsConcurrencyToken &&
        property.ValueGenerated == ValueGenerated.OnAddOrUpdate;

    /// <summary>
    /// "SalesOrderLine" → "sales_order_line", "PK_AspNetUsers" → "pk_asp_net_users", "HTTPRequest" → "http_request".
    /// </summary>
    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            if (char.IsUpper(current))
            {
                var previous = i > 0 ? name[i - 1] : '\0';
                var next = i + 1 < name.Length ? name[i + 1] : '\0';
                var startsWord = i > 0 && previous != '_' &&
                                 (char.IsLower(previous) || char.IsDigit(previous) ||
                                  (char.IsUpper(previous) && char.IsLower(next)));
                if (startsWord)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
