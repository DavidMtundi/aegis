namespace Aegis.Infrastructure.Screening;

using Aegis.Application.Screening;
using Aegis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>
/// Session-level Postgres advisory lock on a dedicated connection, so it is shared by every API instance and held
/// until the handle is disposed, independent of the DbContext's own connection and transactions.
/// </summary>
public sealed class PostgresWatchlistRefreshLock : IWatchlistRefreshLock
{
    public const long LockKey = 0x4145_4749_5352_4631; // "AEGISRF1"

    private readonly string _connectionString;

    public PostgresWatchlistRefreshLock(AegisDbContext db)
        => _connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The Aegis connection string is not configured.");

    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", LockKey);
            if (await command.ExecuteScalarAsync(cancellationToken) is true) return new Handle(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    private sealed class Handle(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", LockKey);
                await command.ExecuteScalarAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
