using System.Text.RegularExpressions;
using Dapper;
using DotNet.Testcontainers.Configurations;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace OnlineShop.Integration.Tests.Infrastructure;

/// <summary>
/// A real SQL Server with two databases, so read and write traffic can be told
/// apart by where the rows actually land.
/// </summary>
/// <remarks>
/// <para>
/// <c>OnlineShop_Primary</c> stands in for the primary and
/// <c>OnlineShop_Replica</c> for the read replica. Nothing replicates between
/// them on its own, which is the point: a row written by a command appears in
/// the primary and is genuinely absent from the replica until
/// <see cref="ReplicateAsync"/> is called. That models unbounded replication
/// lag and makes the routing observable rather than assumed.
/// </para>
/// <para>
/// The server comes from Docker via Testcontainers. Set
/// <c>ONLINESHOP_TEST_SQL</c> to an existing server's master connection string
/// to reuse one instead — useful when iterating, since it skips container
/// startup. Without either, the tests skip rather than fail.
/// </para>
/// </remarks>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string PrimaryDatabase = "OnlineShop_Primary";
    public const string ReplicaDatabase = "OnlineShop_Replica";

    /// <summary>Parent-before-child, so inserts and deletes can walk it in order.</summary>
    private static readonly string[] TablesInDependencyOrder =
    [
        "Tenants",
        "Shops",
        "ShopMembers",
        "Customers",
        "Products",
        "InventoryItems",
        "Carts",
        "CartItems",
        "Orders",
        "OrderItems",
        "Payments",
        "OrderStatusHistory",
        "ShopOrderSequences",
        "Users",
        "UserRoles",
    ];

    /// <summary>
    /// Reference data seeded by the schema, emptied by nothing.
    /// </summary>
    /// <remarks>
    /// Roles are global and created by <c>schema.sql</c>. Wiping them between
    /// tests would leave registration unable to assign anyone a role, so the
    /// reset skips them while replication still copies them.
    /// </remarks>
    private static readonly string[] ReferenceTables = ["Roles"];

    private MsSqlContainer? _container;
    private string? _masterConnectionString;

    /// <summary>False when no SQL Server could be reached; tests skip in that case.</summary>
    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    /// <summary>Connection string for the primary. Backs <c>WriteConnection</c>.</summary>
    public string PrimaryConnectionString => ConnectionStringFor(PrimaryDatabase);

    /// <summary>Connection string for the replica. Backs <c>ReadConnection</c>.</summary>
    public string ReplicaConnectionString => ConnectionStringFor(ReplicaDatabase);

    public async Task InitializeAsync()
    {
        try
        {
            var external = Environment.GetEnvironmentVariable("ONLINESHOP_TEST_SQL");

            if (!string.IsNullOrWhiteSpace(external))
            {
                _masterConnectionString = external;
            }
            else
            {
                _container = await StartContainerAsync();
                _masterConnectionString = _container.GetConnectionString();
            }

            await CreateDatabaseAsync(PrimaryDatabase);
            await CreateDatabaseAsync(ReplicaDatabase);

            var schema = await File.ReadAllTextAsync(SchemaPath());
            await ApplySchemaAsync(PrimaryConnectionString, schema);
            await ApplySchemaAsync(ReplicaConnectionString, schema);

            IsAvailable = true;
        }
        catch (Exception exception)
        {
            // A missing Docker daemon must not turn every integration test into
            // a failure; the tests report themselves as skipped instead.
            IsAvailable = false;
            UnavailableReason =
                $"No SQL Server available for integration tests ({exception.GetType().Name}: {exception.Message}). " +
                "Start Docker, or set ONLINESHOP_TEST_SQL to a master connection string.";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Empties both databases.</summary>
    public async Task ResetAsync()
    {
        await DeleteAllRowsAsync(PrimaryConnectionString);
        await DeleteAllRowsAsync(ReplicaConnectionString);
    }

    /// <summary>
    /// Copies the primary over the replica, standing in for replication having
    /// caught up. Tests that want to observe lag simply do not call it.
    /// </summary>
    public async Task ReplicateAsync()
    {
        await using var connection = new SqlConnection(ConnectionStringFor(ReplicaDatabase));
        await connection.OpenAsync();

        await DeleteAllRowsAsync(connection);

        foreach (var table in TablesInDependencyOrder)
        {
            await connection.ExecuteAsync(
                $"INSERT INTO [{ReplicaDatabase}].dbo.[{table}] " +
                $"SELECT * FROM [{PrimaryDatabase}].dbo.[{table}];");
        }
    }

    /// <summary>
    /// Adds Identity's reference roles if they are missing.
    /// </summary>
    /// <remarks>
    /// Normally seeded by <c>schema.sql</c>. This exists so a test that has
    /// deliberately cleared them can put them back.
    /// </remarks>
    public async Task EnsureRolesAsync()
    {
        foreach (var database in new[] { PrimaryDatabase, ReplicaDatabase })
        {
            await using var connection = new SqlConnection(ConnectionStringFor(database));
            await connection.OpenAsync();

            await connection.ExecuteAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE NormalizedName = 'MERCHANT')
                    INSERT INTO dbo.Roles (Id, Name, NormalizedName, ConcurrencyStamp)
                    VALUES ('9f1d1f27-0f2a-4a55-9d1e-2f6a1c3b4d51', 'Merchant', 'MERCHANT', NEWID());

                IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE NormalizedName = 'SHOPPER')
                    INSERT INTO dbo.Roles (Id, Name, NormalizedName, ConcurrencyStamp)
                    VALUES ('3c7b8e64-5a11-4f0c-8c2d-9b7e5a1d3f42', 'Shopper', 'SHOPPER', NEWID());
                """);
        }
    }

    /// <summary>
    /// Flips the replica between read-only and writable, the way a real
    /// readable secondary behaves.
    /// </summary>
    /// <remarks>
    /// While read-only, any write reaching the replica fails at the server. That
    /// turns "the read path performs no writes" from a claim into something the
    /// engine itself checks.
    /// </remarks>
    public async Task SetReplicaReadOnlyAsync(bool readOnly)
    {
        // Pooled connections would keep the old access mode alive and block the
        // ALTER, which needs exclusive access.
        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(_masterConnectionString);
        await connection.OpenAsync();

        var mode = readOnly ? "READ_ONLY" : "READ_WRITE";
        await connection.ExecuteAsync(
            $"ALTER DATABASE [{ReplicaDatabase}] SET {mode} WITH ROLLBACK IMMEDIATE;");

        SqlConnection.ClearAllPools();
    }

    public string ConnectionStringFor(string database)
    {
        var builder = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = database,
            TrustServerCertificate = true,
        };

        return builder.ConnectionString;
    }

    /// <summary>Opens a connection straight to a database, bypassing the application.</summary>
    public async Task<SqlConnection> OpenAsync(string database)
    {
        var connection = new SqlConnection(ConnectionStringFor(database));
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>
    /// Starts SQL Server, retrying without the resource reaper if that image
    /// cannot be fetched.
    /// </summary>
    /// <remarks>
    /// Testcontainers normally starts a "Ryuk" sidecar that cleans up
    /// containers if the test process is killed outright. That image is hosted
    /// on Docker Hub, which some networks block even when the SQL Server image
    /// itself pulls fine. Losing the reaper only costs cleanup after an
    /// ungraceful exit — this fixture disposes its own container on the normal
    /// path — so it is a better trade than skipping every integration test.
    /// </remarks>
    private static async Task<MsSqlContainer> StartContainerAsync()
    {
        try
        {
            var container = Build();
            await container.StartAsync();
            return container;
        }
        catch when (TestcontainersSettings.ResourceReaperEnabled)
        {
            TestcontainersSettings.ResourceReaperEnabled = false;

            var container = Build();
            await container.StartAsync();
            return container;
        }

        static MsSqlContainer Build() =>
            new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    }

    private async Task CreateDatabaseAsync(string database)
    {
        await using var connection = new SqlConnection(_masterConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(
            $"""
             IF DB_ID('{database}') IS NOT NULL
             BEGIN
                 ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                 DROP DATABASE [{database}];
             END;
             CREATE DATABASE [{database}];
             """);
    }

    private static async Task ApplySchemaAsync(string connectionString, string schema)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        // GO is a batch separator understood by client tools, not by T-SQL.
        var batches = Regex.Split(schema, @"^\s*GO\s*$", RegexOptions.Multiline);

        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            await connection.ExecuteAsync(batch);
        }
    }

    private async Task DeleteAllRowsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await DeleteAllRowsAsync(connection);
    }

    private static async Task DeleteAllRowsAsync(SqlConnection connection)
    {
        foreach (var table in TablesInDependencyOrder.Reverse())
        {
            await connection.ExecuteAsync($"DELETE FROM dbo.[{table}];");
        }
    }

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

        throw new FileNotFoundException("database/schema.sql was not found above the test output directory.");
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
