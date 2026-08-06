using System.Data;
using FicheGen.Core.Abstractions;
using FicheGen.Core.Storage;
using Microsoft.Data.Sqlite;

namespace FicheGen.Infrastructure.Storage;

public sealed class HistoryRepository : IHistoryRepository
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;

    public HistoryRepository(string? dbPath = null)
    {
        var path = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FicheGen",
            "history.db");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ConnectionString;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);

            // Enable WAL mode
            using (var walCmd = connection.CreateCommand())
            {
                walCmd.CommandText = "PRAGMA journal_mode = WAL;";
                await walCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // Schema Migrations Table
            using (var migrationTableCmd = connection.CreateCommand())
            {
                migrationTableCmd.CommandText = """
                    CREATE TABLE IF NOT EXISTS schema_migrations (
                        version INTEGER PRIMARY KEY,
                        applied_utc TEXT NOT NULL
                    );
                    """;
                await migrationTableCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // Migration 1: History + FTS5
            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 1;";
                var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false));

                if (count == 0)
                {
                    using var transaction = connection.BeginTransaction();
                    using var m1Cmd = connection.CreateCommand();
                    m1Cmd.Transaction = transaction;
                    m1Cmd.CommandText = """
                        CREATE TABLE IF NOT EXISTS history (
                            id TEXT PRIMARY KEY,
                            type TEXT NOT NULL,
                            title TEXT NOT NULL,
                            class_level TEXT,
                            subject TEXT,
                            created_utc TEXT NOT NULL,
                            is_favorite INTEGER NOT NULL DEFAULT 0,
                            plain_text TEXT NOT NULL,
                            html TEXT NOT NULL,
                            source_json TEXT,
                            style_preset_id TEXT
                        );

                        CREATE VIRTUAL TABLE IF NOT EXISTS history_fts USING fts5(
                            title,
                            plain_text,
                            content='history',
                            content_rowid='rowid'
                        );

                        CREATE TRIGGER IF NOT EXISTS history_ai AFTER INSERT ON history BEGIN
                            INSERT INTO history_fts(rowid, title, plain_text) VALUES (new.rowid, new.title, new.plain_text);
                        END;

                        CREATE TRIGGER IF NOT EXISTS history_ad AFTER DELETE ON history BEGIN
                            INSERT INTO history_fts(history_fts, rowid, title, plain_text) VALUES('delete', old.rowid, old.title, old.plain_text);
                        END;

                        CREATE TRIGGER IF NOT EXISTS history_au AFTER UPDATE ON history BEGIN
                            INSERT INTO history_fts(history_fts, rowid, title, plain_text) VALUES('delete', old.rowid, old.title, old.plain_text);
                            INSERT INTO history_fts(rowid, title, plain_text) VALUES (new.rowid, new.title, new.plain_text);
                        END;

                        INSERT INTO schema_migrations (version, applied_utc) VALUES (1, datetime('now'));
                        """;
                    await m1Cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                }
            }

            _initialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(HistoryItem item, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO history (id, type, title, class_level, subject, created_utc, is_favorite, plain_text, html, source_json, style_preset_id)
            VALUES (@id, @type, @title, @class_level, @subject, @created_utc, @is_favorite, @plain_text, @html, @source_json, @style_preset_id)
            ON CONFLICT(id) DO UPDATE SET
                type = excluded.type,
                title = excluded.title,
                class_level = excluded.class_level,
                subject = excluded.subject,
                created_utc = excluded.created_utc,
                is_favorite = excluded.is_favorite,
                plain_text = excluded.plain_text,
                html = excluded.html,
                source_json = excluded.source_json,
                style_preset_id = excluded.style_preset_id;
            """;

        cmd.Parameters.AddWithValue("@id", item.Id);
        cmd.Parameters.AddWithValue("@type", item.Type);
        cmd.Parameters.AddWithValue("@title", item.Title);
        cmd.Parameters.AddWithValue("@class_level", (object?)item.ClassLevel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@subject", (object?)item.Subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created_utc", item.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@is_favorite", item.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@plain_text", item.PlainText);
        cmd.Parameters.AddWithValue("@html", item.Html);
        cmd.Parameters.AddWithValue("@source_json", (object?)item.SourceJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@style_preset_id", (object?)item.StylePresetId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<HistoryItem?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM history WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return ReadItem(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<HistoryItem>> SearchAsync(
        string? query = null,
        string? typeFilter = null,
        bool isFavoriteOnly = false,
        int limit = 50,
        int offset = 0,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var hasQuery = !string.IsNullOrWhiteSpace(query);
        using var cmd = connection.CreateCommand();

        var sql = hasQuery
            ? """
              SELECT h.* FROM history h
              JOIN history_fts fts ON h.rowid = fts.rowid
              WHERE history_fts MATCH @query
              """
            : "SELECT h.* FROM history h WHERE 1=1";

        if (!string.IsNullOrEmpty(typeFilter))
        {
            sql += " AND h.type = @typeFilter";
            cmd.Parameters.AddWithValue("@typeFilter", typeFilter);
        }

        if (isFavoriteOnly)
        {
            sql += " AND h.is_favorite = 1";
        }

        sql += hasQuery
            ? " ORDER BY bm25(history_fts) LIMIT @limit OFFSET @offset;"
            : " ORDER BY h.created_utc DESC LIMIT @limit OFFSET @offset;";

        if (hasQuery)
        {
            var sanitizedQuery = query!.Replace("\"", "").Trim();
            cmd.Parameters.AddWithValue("@query", $"{sanitizedQuery}*");
        }

        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        cmd.CommandText = sql;

        var results = new List<HistoryItem>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(ReadItem(reader));
        }

        return results.AsReadOnly();
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM history WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RenameAsync(string id, string newTitle, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE history SET title = @title WHERE id = @id;";
        cmd.Parameters.AddWithValue("@title", newTitle);
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ToggleFavoriteAsync(string id, bool isFavorite, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE history SET is_favorite = @fav WHERE id = @id;";
        cmd.Parameters.AddWithValue("@fav", isFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CleanupRetentionAsync(int retentionDays, CancellationToken ct = default)
    {
        if (retentionDays <= 0) return 0;

        await InitializeAsync(ct).ConfigureAwait(false);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays).ToString("O");

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM history WHERE is_favorite = 0 AND created_utc < @cutoff;";
        cmd.Parameters.AddWithValue("@cutoff", cutoff);

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static HistoryItem ReadItem(SqliteDataReader reader)
    {
        return new HistoryItem
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Type = reader.GetString(reader.GetOrdinal("type")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            ClassLevel = reader.IsDBNull(reader.GetOrdinal("class_level")) ? null : reader.GetString(reader.GetOrdinal("class_level")),
            Subject = reader.IsDBNull(reader.GetOrdinal("subject")) ? null : reader.GetString(reader.GetOrdinal("subject")),
            CreatedUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_utc"))),
            IsFavorite = reader.GetInt32(reader.GetOrdinal("is_favorite")) == 1,
            PlainText = reader.GetString(reader.GetOrdinal("plain_text")),
            Html = reader.GetString(reader.GetOrdinal("html")),
            SourceJson = reader.IsDBNull(reader.GetOrdinal("source_json")) ? null : reader.GetString(reader.GetOrdinal("source_json")),
            StylePresetId = reader.IsDBNull(reader.GetOrdinal("style_preset_id")) ? null : reader.GetString(reader.GetOrdinal("style_preset_id")),
        };
    }
}
