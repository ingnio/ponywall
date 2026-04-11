using System;
using System.IO;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace pylorak.TinyWall.History
{
    /// <summary>
    /// Runs the embedded-resource SQL migrations against the event-store
    /// database. See Docs/EXPLAINABILITY.md section 7.
    /// </summary>
    internal static class EventStoreSchema
    {
        /// <summary>
        /// Current in-code schema version. Every row written into
        /// events_hot / events_warm stamps this value so old readers can
        /// degrade gracefully and new readers can detect older rows.
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        private const string MigrationResourcePrefix = "pylorak.TinyWall.History.Migrations.";

        public static void EnsureSchema(SqliteConnection connection)
        {
            // Apply PRAGMAs first so the migration runs under WAL.
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
                pragma.ExecuteNonQuery();
            }

            int stored = ReadStoredSchemaVersion(connection);
            if (stored >= CurrentSchemaVersion)
                return;

            // Migrations are numbered; for now we only ship 001_initial.sql.
            // Future migrations should be appended here in order.
            ApplyMigration(connection, "001_initial.sql");
        }

        private static int ReadStoredSchemaVersion(SqliteConnection connection)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
                var result = cmd.ExecuteScalar();
                if (result is string s && int.TryParse(s, out int v))
                    return v;
            }
            catch (SqliteException)
            {
                // meta table doesn't exist yet — this is a fresh DB.
            }
            return 0;
        }

        private static void ApplyMigration(SqliteConnection connection, string resourceName)
        {
            string sql = LoadEmbeddedSql(resourceName);
            using var tx = connection.BeginTransaction();
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        private static string LoadEmbeddedSql(string fileName)
        {
            var asm = typeof(EventStoreSchema).Assembly;
            string resource = MigrationResourcePrefix + fileName;
            using var stream = asm.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Embedded migration resource '{resource}' not found. Available: {string.Join(", ", asm.GetManifestResourceNames())}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
