using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.Infrastructure.Services;

public class StorageDisplayService
{
    public const string RestrictedLabel =
        "Permanent storage (restricted)";

    private readonly Func<ManifestDbContext> _createContext;

    public StorageDisplayService()
        : this(DevelopmentDatabase.CreateContext)
    {
    }

    public StorageDisplayService(
        Func<ManifestDbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        _createContext = createContext;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetLabelsAsync(
        Guid userId)
    {
        await using var database = _createContext();

        var user = await database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId);

        if (user is null ||
            !user.IsActive ||
            (user.Role != UserRole.Officer &&
             user.Role != UserRole.EvidenceCustodian &&
             user.Role != UserRole.Administrator))
        {
            throw new InvalidOperationException(
                "An active account with property access is required.");
        }

        bool patrol = user.Role == UserRole.Officer;

        var visibleQuery = database.StorageLocations.AsNoTracking();

        if (patrol)
        {
            visibleQuery = visibleQuery.Where(x =>
                x.LocationType == StorageLocationType.Temporary);
        }

        // Include inactive locations so historical movements
        // can still display their permitted location names.
        var visibleLocations = await visibleQuery.ToListAsync();

        var visibleById = visibleLocations.ToDictionary(x => x.Id);
        var restrictedIds = new HashSet<Guid>();

        if (patrol)
        {
            // Only IDs are retrieved for restricted locations.
            // Permanent location names are not loaded for patrol.
            var ids = await database.StorageLocations
                .AsNoTracking()
                .Where(x =>
                    x.LocationType != StorageLocationType.Temporary)
                .Select(x => x.Id)
                .ToListAsync();

            restrictedIds.UnionWith(ids);
        }

        var labels = new Dictionary<Guid, string>();

        foreach (Guid id in restrictedIds)
        {
            labels[id] = RestrictedLabel;
        }

        foreach (var location in visibleLocations)
        {
            labels[location.Id] = BuildPath(
                location.Id,
                visibleById,
                restrictedIds);
        }

        return labels;
    }

    public static string GetLabel(
        Guid? locationId,
        IReadOnlyDictionary<Guid, string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        if (locationId is null)
        {
            return "None";
        }

        return labels.TryGetValue(locationId.Value, out string? label)
            ? label
            : "Storage location unavailable";
    }

    private static string BuildPath(
        Guid locationId,
        IReadOnlyDictionary<Guid, StorageLocation> visibleById,
        HashSet<Guid> restrictedIds)
    {
        var parts = new List<string>();
        var visited = new HashSet<Guid>();
        Guid? currentId = locationId;

        while (currentId is Guid id)
        {
            if (!visited.Add(id))
            {
                parts.Add("[Location cycle]");
                break;
            }

            if (restrictedIds.Contains(id))
            {
                parts.Add("[Restricted parent]");
                break;
            }

            if (!visibleById.TryGetValue(id, out var location))
            {
                parts.Add("[Location unavailable]");
                break;
            }

            parts.Add(location.Name);
            currentId = location.ParentLocationId;
        }

        parts.Reverse();
        return string.Join(" / ", parts);
    }
}