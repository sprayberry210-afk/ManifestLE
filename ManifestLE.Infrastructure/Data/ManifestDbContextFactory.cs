using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ManifestLE.Infrastructure.Data;

public class ManifestDbContextFactory
    : IDesignTimeDbContextFactory<ManifestDbContext>
{
    public ManifestDbContext CreateDbContext(string[] args)
    {
        string databaseFolder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "ManifestLE",
            "Development");

        Directory.CreateDirectory(databaseFolder);

        string databasePath = Path.Combine(
            databaseFolder,
            "manifestle-development.db");

        var optionsBuilder =
            new DbContextOptionsBuilder<ManifestDbContext>();

        optionsBuilder.UseSqlite(
            $"Data Source={databasePath};Foreign Keys=True");

        return new ManifestDbContext(optionsBuilder.Options);
    }
}