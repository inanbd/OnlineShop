using System.Reflection;
using System.Text.RegularExpressions;

namespace OnlineShop.Architecture.Tests;

/// <summary>
/// Reads every SQL statement in the persistence layer and checks it is tenant
/// scoped and parameterised.
/// </summary>
/// <remarks>
/// A code review catches a missing <c>TenantId</c> predicate only if the
/// reviewer is paying attention on the day. This catches it every build, and
/// it covers SQL added later by someone who never read the rule.
/// </remarks>
public sealed class TenantIsolationSqlTests
{
    /// <summary>
    /// The SQL constants that are legitimately not tenant-scoped, each with the
    /// reason it cannot be.
    /// </summary>
    /// <remarks>
    /// Every entry is a place where there is no tenant to filter by yet, or
    /// where the row being touched is what defines a tenant. The list is
    /// asserted exactly by <see cref="The_documented_exceptions_are_exactly_these"/>,
    /// so a new one cannot be added without someone changing this test and
    /// writing down why.
    /// </remarks>
    private static readonly Dictionary<string, string> GloballyScopedStatements = new(StringComparer.Ordinal)
    {
        ["TenantRepository.SlugExistsSql"] =
            "A tenant slug identifies the tenant itself, so uniqueness cannot be scoped to one.",

        ["ShopDirectory.FindBySlugSql"] =
            "Resolves a public storefront URL to a tenant. This is the lookup that establishes the tenant for " +
            "the request, so there is nothing to filter by yet. It exposes only public shop identity.",

        ["ShopDirectory.ListActiveSql"] =
            "The public list of storefronts, which spans tenants by definition. Exposes only public shop identity.",

        ["DapperUserStore.SelectColumns"] =
            "A shared SELECT fragment with no WHERE clause of its own; the statements built from it carry the " +
            "predicate.",

        ["DapperUserStore.DeleteUserRolesSql"] =
            "UserRoles has no TenantId because roles are global reference data. Keyed by the globally unique " +
            "user id instead. The companion DeleteUserSql is tenant-filtered and is still checked.",

        ["DapperUserStore.FindByIdSql"] =
            "Authentication happens before a tenant is known; the user row is what carries the tenant.",

        ["DapperUserStore.FindByNameSql"] =
            "Sign-in by username, before any tenant is known.",

        ["DapperUserStore.FindByEmailSql"] =
            "Sign-in by email address, before any tenant is known.",

        ["DapperUserStore.AddToRoleSql"] =
            "Roles are global reference data ('Merchant', 'Shopper'), not tenant-owned.",

        ["DapperUserStore.RemoveFromRoleSql"] = "Roles are global reference data.",
        ["DapperUserStore.GetRolesSql"] = "Roles are global reference data.",
        ["DapperUserStore.IsInRoleSql"] = "Roles are global reference data.",
        ["DapperUserStore.GetUsersInRoleSql"] = "Roles are global reference data.",

        ["DapperRoleStore.SelectColumns"] = "Roles are global reference data.",
        ["DapperRoleStore.FindByIdSql"] = "Roles are global reference data.",
        ["DapperRoleStore.FindByNameSql"] = "Roles are global reference data.",
        ["DapperRoleStore.InsertSql"] = "Roles are global reference data.",
        ["DapperRoleStore.UpdateSql"] = "Roles are global reference data.",
        ["DapperRoleStore.DeleteSql"] = "Roles are global reference data.",
    };

    [Fact]
    public void The_documented_exceptions_are_exactly_these()
    {
        // Pins the exception list. Adding a tenant-agnostic statement means
        // editing this test, which is the point: it should never be a quiet
        // side effect of writing a query.
        string[] expected =
        [
            "DapperRoleStore.DeleteSql",
            "DapperRoleStore.FindByIdSql",
            "DapperRoleStore.FindByNameSql",
            "DapperRoleStore.InsertSql",
            "DapperRoleStore.SelectColumns",
            "DapperRoleStore.UpdateSql",
            "DapperUserStore.AddToRoleSql",
            "DapperUserStore.DeleteUserRolesSql",
            "DapperUserStore.FindByEmailSql",
            "DapperUserStore.FindByIdSql",
            "DapperUserStore.FindByNameSql",
            "DapperUserStore.GetRolesSql",
            "DapperUserStore.GetUsersInRoleSql",
            "DapperUserStore.IsInRoleSql",
            "DapperUserStore.RemoveFromRoleSql",
            "DapperUserStore.SelectColumns",
            "ShopDirectory.FindBySlugSql",
            "ShopDirectory.ListActiveSql",
            "TenantRepository.SlugExistsSql",
        ];

        Assert.Equal(expected, GloballyScopedStatements.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.All(GloballyScopedStatements.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }

    public static TheoryData<string, string> AllSqlStatements()
    {
        var data = new TheoryData<string, string>();

        foreach (var (owner, sql) in SqlConstants())
        {
            foreach (var statement in SplitStatements(sql))
            {
                data.Add(owner, statement);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllSqlStatements))]
    public void Every_statement_is_tenant_scoped(string owner, string statement)
    {
        if (GloballyScopedStatements.ContainsKey(owner))
        {
            return;
        }

        Assert.True(
            statement.Contains("@TenantId", StringComparison.Ordinal),
            $"""
             A SQL statement in {owner} does not reference @TenantId.

             Every tenant-owned read and write must be filtered by TenantId. Filtering on
             'WHERE Id = @Id' alone lets one tenant reach another tenant's row whenever an
             identifier leaks or is guessed.

             Statement:
             {statement}
             """);
    }

    [Theory]
    [MemberData(nameof(AllSqlStatements))]
    public void No_statement_interpolates_a_value(string owner, string statement)
    {
        // Every value reaches SQL Server as a parameter. A literal quote in a
        // statement is the shape of string concatenation, which is how
        // injection gets in. The only quoted literals in this layer are the
        // ESCAPE clauses that accompany LIKE.
        var withoutEscapeClauses = Regex.Replace(
            statement,
            @"ESCAPE\s+'\\'",
            string.Empty,
            RegexOptions.IgnoreCase);

        Assert.False(
            withoutEscapeClauses.Contains('\''),
            $"""
             A SQL statement in {owner} contains a quoted literal. All values must be passed
             as parameters rather than embedded in the statement text.

             Statement:
             {statement}
             """);
    }

    [Fact]
    public void The_persistence_layer_actually_has_sql_to_check()
    {
        // Guards the test itself: if the reflection below stopped finding SQL,
        // every assertion above would pass vacuously.
        var statements = AllSqlStatements();

        Assert.True(
            statements.Count > 30,
            $"Only {statements.Count} SQL statements were discovered, which suggests the reflection that " +
            "finds them has stopped working rather than that the layer shrank.");
    }

    /// <summary>
    /// The persistence namespaces whose SQL is checked. Identity is included:
    /// its stores are hand-written Dapper too, and its user table is
    /// tenant-owned even though its role tables are not.
    /// </summary>
    private static readonly string[] ScannedNamespaces =
    [
        "OnlineShop.Persistence.Queries",
        "OnlineShop.Persistence.Repositories",
        "OnlineShop.Persistence.Identity",
    ];

    /// <summary>
    /// Finds every <c>const string</c> holding SQL in the queries, repositories
    /// and Identity stores, including the ones on private nested types.
    /// </summary>
    private static IEnumerable<(string Owner, string Sql)> SqlConstants()
    {
        var assembly = typeof(Persistence.Connections.IDbConnectionFactory).Assembly;

        var relevantTypes = assembly
            .GetTypes()
            .Where(type => type.Namespace is not null
                           && ScannedNamespaces.Any(ns => type.Namespace.StartsWith(ns, StringComparison.Ordinal)));

        foreach (var type in relevantTypes)
        {
            var constants = type
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string));

            foreach (var constant in constants)
            {
                if (constant.GetRawConstantValue() is not string value)
                {
                    continue;
                }

                if (!LooksLikeSql(value))
                {
                    continue;
                }

                yield return ($"{type.Name}.{constant.Name}", value);
            }
        }
    }

    private static bool LooksLikeSql(string value)
    {
        return value.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
               || value.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
               || value.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
               || value.Contains("DELETE", StringComparison.OrdinalIgnoreCase)
               || value.Contains("MERGE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Splits a batch into individual statements, dropping line comments first
    /// so a trailing comment is not mistaken for a statement of its own.
    /// </summary>
    private static IEnumerable<string> SplitStatements(string sql)
    {
        var withoutComments = Regex.Replace(sql, @"--[^\r\n]*", string.Empty);

        return withoutComments
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(statement => statement.Length > 0);
    }
}
