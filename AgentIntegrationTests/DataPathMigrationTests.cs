using AgentControl;
using System.Data.SQLite;

namespace AgentIntegrationTests;

public sealed class DataPathMigrationTests
{
    [Fact]
    public void ControlDatabaseMigration_IncludesCommittedWalContent()
    {
        string legacyRoot = TestEnvironment.CreateDirectory("legacy-control-db");
        string fileName = "Legacy-" + Guid.NewGuid().ToString("N") + ".db";
        string sourcePath = Path.Combine(legacyRoot, fileName);
        string oldCurrentDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = legacyRoot;
            using SQLiteConnection source = Open(sourcePath);
            using (SQLiteCommand pragma = new SQLiteCommand(
                       "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;",
                       source))
            {
                pragma.ExecuteNonQuery();
            }
            using (SQLiteCommand create = new SQLiteCommand(
                       "CREATE TABLE Sample(Value TEXT NOT NULL);",
                       source))
            {
                create.ExecuteNonQuery();
            }
            using (SQLiteCommand insert = new SQLiteCommand(
                       "INSERT INTO Sample(Value) VALUES ('from-wal');",
                       source))
            {
                insert.ExecuteNonQuery();
            }

            string migratedPath = ControlDataPaths.GetDatabasePath(fileName);
            using SQLiteConnection migrated = Open(migratedPath);
            using SQLiteCommand read = new SQLiteCommand("SELECT Value FROM Sample LIMIT 1;", migrated);

            Assert.Equal("from-wal", read.ExecuteScalar()?.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = oldCurrentDirectory;
        }
    }

    private static SQLiteConnection Open(string path)
    {
        var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
        {
            DataSource = path,
            Version = 3,
            DefaultTimeout = 30
        }.ConnectionString);
        connection.Open();
        return connection;
    }
}
