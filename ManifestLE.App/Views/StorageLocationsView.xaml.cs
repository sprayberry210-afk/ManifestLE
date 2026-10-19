using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.App.Views;

public partial class StorageLocationsView : UserControl
{
    private readonly Guid _userId;
    private readonly List<StorageLocation> _locations = new();

    private bool _isSaving;
    private bool _canManage;

    public StorageLocationsView()
        : this(Guid.Empty)
    {
    }

    public StorageLocationsView(Guid userId)
    {
        InitializeComponent();

        _userId = userId;

        LocationTypeBox.ItemsSource = new[]
        {
            new LocationTypeChoice(
                StorageLocationType.Permanent,
                "Permanent"),
            new LocationTypeChoice(
                StorageLocationType.Temporary,
                "Temporary")
        };

        LocationTypeBox.SelectedIndex = 0;
        Loaded += StorageLocationsView_Loaded;
    }

    private static bool CanManage(AppUser? user)
    {
        return user is not null &&
               user.IsActive &&
               (user.Role == UserRole.Administrator ||
                user.Role == UserRole.EvidenceCustodian);
    }

    private async void StorageLocationsView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= StorageLocationsView_Loaded;

        try
        {
            await LoadLocationsAsync();

            if (!_canManage)
            {
                ShowStatus(
                    "You can view temporary intake locations. " +
                    "Only authorized staff can manage storage locations.",
                    false);
            }
        }
        catch (Exception)
        {
            _canManage = false;
            _locations.Clear();
            LocationsGrid.ItemsSource = null;
            ParentBox.ItemsSource = null;

            ShowStatus(
                "Storage locations could not be loaded.",
                true);
        }
        finally
        {
            UpdateControls();
        }
    }

    private async Task LoadLocationsAsync()
    {
        _canManage = false;
        _locations.Clear();
        LocationsGrid.ItemsSource = null;
        ParentBox.ItemsSource = null;
        UpdateControls();

        await using var database =
            DevelopmentDatabase.CreateContext();

        var user = await database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == _userId);

        if (user is null || !user.IsActive)
        {
            throw new InvalidOperationException(
                "An active signed-in account is required.");
        }

        bool canManage = CanManage(user);

        var query = database.StorageLocations
            .AsNoTracking()
            .Where(x => x.IsActive);

        if (!canManage)
        {
            query = query.Where(
                x => x.LocationType == StorageLocationType.Temporary);
        }

        var locations = await query
            .OrderBy(x => x.Name)
            .ToListAsync();

        _locations.AddRange(locations);

        var namesById = locations.ToDictionary(
            x => x.Id,
            x => x.Name);

        LocationsGrid.ItemsSource = locations.Select(x => new
        {
            x.Name,
            LocationType = x.LocationType.ToString(),
            ParentName = x.ParentLocationId is Guid parentId
                ? namesById.GetValueOrDefault(parentId, "—")
                : "—",
            x.Description
        }).ToList();

        _canManage = canManage;
        RebuildParentChoices();
        UpdateControls();
    }

    private void LocationTypeBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        RebuildParentChoices();
    }

    private void RebuildParentChoices()
    {
        if (ParentBox is null)
        {
            return;
        }

        var choices = new List<ParentChoice>
        {
            new(null, "(No parent — top level)")
        };

        if (_canManage &&
            LocationTypeBox.SelectedItem is LocationTypeChoice type)
        {
            choices.AddRange(
                _locations
                    .Where(x => x.LocationType == type.Value)
                    .Select(x => new ParentChoice(
                        x.Id,
                        BuildLocationPath(x)))
                    .OrderBy(x => x.Name));
        }

        ParentBox.ItemsSource = choices;
        ParentBox.SelectedIndex = 0;
    }

    private string BuildLocationPath(StorageLocation location)
    {
        var parts = new List<string>();
        var visited = new HashSet<Guid>();
        StorageLocation? current = location;

        while (current is not null && visited.Add(current.Id))
        {
            parts.Add(current.Name);

            current = current.ParentLocationId is Guid parentId
                ? _locations.FirstOrDefault(x =>
                    x.Id == parentId &&
                    x.LocationType == location.LocationType)
                : null;
        }

        parts.Reverse();
        return string.Join(" / ", parts);
    }

    private async void SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSaving || !_canManage)
        {
            return;
        }

        StatusText.Visibility = Visibility.Collapsed;

        string name = NameBox.Text.Trim();
        string description = DescriptionBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowStatus("Enter a location name.", true);
            NameBox.Focus();
            return;
        }

        if (name.Length > 100 || description.Length > 500)
        {
            ShowStatus(
                "Use at most 100 characters for the name " +
                "and 500 for the description.",
                true);
            return;
        }

        if (LocationTypeBox.SelectedItem is not LocationTypeChoice type ||
            !Enum.IsDefined(typeof(StorageLocationType), type.Value))
        {
            ShowStatus("Choose a valid location type.", true);
            return;
        }

        if (ParentBox.SelectedItem is not ParentChoice parent)
        {
            ShowStatus("Choose a parent location or no parent.", true);
            return;
        }

        _isSaving = true;
        UpdateControls();

        bool saved = false;

        try
        {
            await using var database =
                DevelopmentDatabase.CreateContext();

            await using var transaction =
                await database.Database.BeginTransactionAsync();

            var user = await database.Users
                .SingleOrDefaultAsync(x => x.Id == _userId);

            if (!CanManage(user))
            {
                _canManage = false;

                ShowStatus(
                    "You do not have permission to add locations.",
                    true);
                return;
            }

            if (parent.Id is Guid parentId)
            {
                var parentLocation = await database.StorageLocations
                    .SingleOrDefaultAsync(x => x.Id == parentId);

                if (parentLocation is null ||
                    !parentLocation.IsActive ||
                    parentLocation.LocationType != type.Value)
                {
                    ShowStatus(
                        "The parent must be an active location " +
                        "with the same type. Reopen this screen.",
                        true);
                    return;
                }
            }

            var siblingNames = await database.StorageLocations
                .Where(x => x.ParentLocationId == parent.Id)
                .Select(x => x.Name)
                .ToListAsync();

            if (siblingNames.Any(existing =>
                string.Equals(
                    existing.Trim(),
                    name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                ShowStatus(
                    "A location with this name already exists " +
                    "under the selected parent.",
                    true);
                return;
            }

            var location = new StorageLocation
            {
                Name = name,
                Description = description,
                ParentLocationId = parent.Id,
                LocationType = type.Value,
                IsActive = true
            };

            database.StorageLocations.Add(location);

            database.AuditLogEntries.Add(new AuditLogEntry
            {
                UserId = _userId,
                Action = "StorageLocation.Created",
                EntityType = nameof(StorageLocation),
                EntityId = location.Id,
                Details =
                    $"Storage location created: {name}. " +
                    $"Type: {type.Value}. " +
                    $"Parent: {parent.Id?.ToString() ?? "None"}."
            });

            await database.SaveChangesAsync();
            await transaction.CommitAsync();

            saved = true;
        }
        catch (Exception)
        {
            ShowStatus("The location could not be saved.", true);
        }
        finally
        {
            _isSaving = false;
            UpdateControls();
        }

        if (!saved)
        {
            return;
        }

        NameBox.Clear();
        DescriptionBox.Clear();

        try
        {
            await LoadLocationsAsync();
            ShowStatus("Location added.", false);
            NameBox.Focus();
        }
        catch (Exception)
        {
            _canManage = false;
            _locations.Clear();
            LocationsGrid.ItemsSource = null;
            ParentBox.ItemsSource = null;
            UpdateControls();

            ShowStatus(
                "Location saved, but the table could not refresh. " +
                "Reopen this screen.",
                true);
        }
    }

    private void UpdateControls()
    {
        bool enabled = _canManage && !_isSaving;

        FormPanel.IsEnabled = enabled;
        SaveButton.IsEnabled = enabled;
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(255, 155, 155))
            : new SolidColorBrush(Color.FromRgb(134, 239, 172));

        StatusText.Visibility = Visibility.Visible;
    }

    private sealed record ParentChoice(Guid? Id, string Name);

    private sealed record LocationTypeChoice(
        StorageLocationType Value,
        string Name);
}