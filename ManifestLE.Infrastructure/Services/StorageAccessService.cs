using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.Infrastructure.Services;

public static class StorageAccessService
{
    public static async Task<List<StorageLocation>>
        GetIntakeLocationsAsync(
            ManifestDbContext database,
            Guid userId)
    {
        ArgumentNullException.ThrowIfNull(database);

        var user = await RequireIntakeUserAsync(
            database,
            userId);

        var query = database.StorageLocations
            .AsNoTracking()
            .Where(x => x.IsActive);

        if (user.Role == UserRole.Officer)
        {
            query = query.Where(x =>
                x.LocationType == StorageLocationType.Temporary);
        }
        else
        {
            query = query.Where(x =>
                x.LocationType == StorageLocationType.Temporary ||
                x.LocationType == StorageLocationType.Permanent);
        }

        return await query
            .OrderBy(x => x.Name)
            .ToListAsync();
    }

    public static async Task<StorageLocation>
        RequireIntakeLocationAsync(
            ManifestDbContext database,
            Guid userId,
            Guid locationId)
    {
        ArgumentNullException.ThrowIfNull(database);

        var user = await RequireIntakeUserAsync(
            database,
            userId);

        var location = await database.StorageLocations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == locationId);

        if (location is null ||
            !location.IsActive ||
            !Enum.IsDefined(
                typeof(StorageLocationType),
                location.LocationType))
        {
            throw new InvalidOperationException(
                "The selected storage location is unavailable. " +
                "Reopen intake and choose an available location.");
        }

        if (user.Role == UserRole.Officer &&
            location.LocationType != StorageLocationType.Temporary)
        {
            throw new InvalidOperationException(
                "Patrol intake must be submitted to an active " +
                "temporary storage locker.");
        }

        return location;
    }

    private static async Task<AppUser> RequireIntakeUserAsync(
        ManifestDbContext database,
        Guid userId)
    {
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
                "An active account with intake permission is required.");
        }

        return user;
    }
}