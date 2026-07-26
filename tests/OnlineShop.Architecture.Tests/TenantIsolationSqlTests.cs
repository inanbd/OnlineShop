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
    /// Statements that are legitimately not tenant-scoped, each with the reason.
    /// Empty today: every statement in the layer touches tenant-owned data.
    /// Anything added here should be obviously global, such as a lookup of
    /// server metadata.
    /// </summary>
    private static readonly HashSet<string> GloballyScopedStatements = new(StringComparer.Ordinal);

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
        if (GloballyScopedStatements.Contains(statement))
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
    /// Finds every <c>const string</c> holding SQL in the queries and
    /// repositories, including the ones on private nested types.
    /// </summary>
    private static IEnumerable<(string Owner, string Sql)> SqlConstants()
    {
        var assembly = typeof(Persistence.Connections.IDbConnectionFactory).Assembly;

        var relevantTypes = assembly
            .GetTypes()
            .Where(type => type.Namespace is not null
                           && (type.Namespace.StartsWith("OnlineShop.Persistence.Queries", StringComparison.Ordinal)
                               || type.Namespace.StartsWith("OnlineShop.Persistence.Repositories", StringComparison.Ordinal)));

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
