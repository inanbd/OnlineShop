using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace OnlineShop.Architecture.Tests;

/// <summary>
/// Parses every SQL statement in the solution with the T-SQL parser SQL Server
/// ships.
/// </summary>
/// <remarks>
/// SQL held in string constants is invisible to the C# compiler: a missing
/// comma or an unbalanced parenthesis compiles perfectly and fails at run time,
/// on whichever code path happens to reach it. This turns that into a build
/// failure, and it needs no database to do it.
/// </remarks>
public sealed class SqlSyntaxTests
{
    public static TheoryData<string, string> AllApplicationSql()
    {
        var data = new TheoryData<string, string>();

        foreach (var (owner, sql) in SqlConstants())
        {
            data.Add(owner, sql);
        }

        return data;
    }

    public static TheoryData<string, string> AllSchemaBatches()
    {
        var data = new TheoryData<string, string>();

        var schemaPath = SchemaPath();
        var schema = File.ReadAllText(schemaPath);

        // GO is a batch separator understood by client tools, not T-SQL itself,
        // so the parser sees one batch at a time.
        var batches = Regex.Split(schema, @"^\s*GO\s*$", RegexOptions.Multiline);

        for (var index = 0; index < batches.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(batches[index]))
            {
                continue;
            }

            data.Add($"schema.sql batch {index}", batches[index]);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllApplicationSql))]
    public void Every_statement_parses(string owner, string sql) => AssertParses(owner, sql);

    [Theory]
    [MemberData(nameof(AllSchemaBatches))]
    public void Every_schema_batch_parses(string owner, string sql) => AssertParses(owner, sql);

    [Fact]
    public void The_parser_would_notice_a_broken_statement()
    {
        // Keeps the two theories above honest: if the parser were silently
        // accepting everything, they would pass no matter what the SQL said.
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader("SELECT FROM WHERE dbo..Products ((;");

        parser.Parse(reader, out var errors);

        Assert.NotEmpty(errors);
    }

    private static void AssertParses(string owner, string sql)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);

        parser.Parse(reader, out var errors);

        Assert.True(
            errors.Count == 0,
            $"""
             SQL in {owner} does not parse:

             {string.Join(Environment.NewLine, errors.Select(e => $"  line {e.Line}, col {e.Column}: {e.Message}"))}

             {sql}
             """);
    }

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
                if (constant.GetRawConstantValue() is not string value || !LooksLikeSql(value))
                {
                    continue;
                }

                yield return ($"{type.Name}.{constant.Name}", value);
            }
        }
    }

    private static bool LooksLikeSql(string value) =>
        value.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
        || value.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
        || value.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
        || value.Contains("DELETE", StringComparison.OrdinalIgnoreCase)
        || value.Contains("MERGE", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Walks up from the test binary to the repository root to find the schema.
    /// </summary>
    private static string SchemaPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "database", "schema.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "database/schema.sql was not found above the test output directory.");
    }
}
