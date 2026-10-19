using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.Infrastructure.Data;

public static class DevelopmentDatabase
{
    public static ManifestDbContext CreateContext()
    {
        string databasePath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "ManifestLE",
            "Development",
            "manifestle-development.db");

        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException(
                "The development database was not found.",
                databasePath);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true
        };

        var options = new DbContextOptionsBuilder<ManifestDbContext>()
            .UseSqlite(connectionString.ToString())
            .Options;

        return new ManifestDbContext(options);
    }
}