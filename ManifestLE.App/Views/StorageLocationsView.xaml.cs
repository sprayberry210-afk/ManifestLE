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
    private bool _isSaving;

    public StorageLocationsView()
        : this(Guid.Empty)
    {
    }

    public StorageLocationsView(Guid userId)
    {
        InitializeComponent();

        _userId = userId;
        Loaded += StorageLocationsView_Loaded;
    }

    private async void StorageLocationsView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= StorageLocationsView_Loaded;

        try
        {
            await LoadLocationsAsync();

            await using var database =
                DevelopmentDatabase.CreateContext();

            var user = await database.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == _userId);

            bool canManage = user is not null &&
                user.IsActive &&
                (user.Role == UserRole.Administrator ||
                 user.Role == UserRole.EvidenceCustodian);

            FormPanel.IsEnabled = canManage;
            SaveButton.IsEnabled = canManage;

            if (!canManage)
            {
                ShowStatus(
                    "Only administrators and evidence custodians " +
                    "can add storage locations.",
                    true);
            }
        }
        catch (Exception)
        {
            SaveButton.IsEnabled = false;
            ShowStatus("Storage locations could not be loaded.", true);
        }
    }

    private async Task LoadLocationsAsync()
    {
        await using var database =
            DevelopmentDatabase.CreateContext();

        var locations = await database.StorageLocations
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();

        var namesById = locations.ToDictionary(
            x => x.Id,
            x => x.Name);

        LocationsGrid.ItemsSource = locations.Select(x => new
        {
            x.Name,
            ParentName = x.ParentLocationId is Guid parentId
                ? namesById.GetValueOrDefault(
                    parentId,
                    "Inactive parent")
                : "—",
            x.Description
        }).ToList();

        var parentChoices = new List<ParentChoice>
        {
            new ParentChoice
            {
                Id = null,
                Name = "(No parent — top level)"
            }
        };

        parentChoices.AddRange(locations.Select(x => new ParentChoice
        {
            Id = x.Id,
            Name = BuildLocationPath(x, locations)
        }));

        ParentBox.ItemsSource = parentChoices;
        ParentBox.SelectedIndex = 0;
    }

    private static string BuildLocationPath(
        StorageLocation location,
        List<StorageLocation> locations)
    {
        var parts = new List<string>();
        var visited = new HashSet<Guid>();
        StorageLocation? current = location;

        while (current is not null && visited.Add(current.Id))
        {
            parts.Add(current.Name);

            current = current.ParentLocationId is Guid parentId
                ? locations.FirstOrDefault(x => x.Id == parentId)
                : null;
        }

        parts.Reverse();
        return string.Join(" / ", parts);
    }

    private async void SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSaving)
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

        if (ParentBox.SelectedItem is not ParentChoice parent)
        {
            ShowStatus("Choose a parent location or no parent.", true);
            return;
        }

        _isSaving = true;
        SaveButton.IsEnabled = false;
        FormPanel.IsEnabled = false;

        bool saved = false;

        try
        {
            await using var database =
                DevelopmentDatabase.CreateContext();

            await using var transaction =
                await database.Database.BeginTransactionAsync();

            var user = await database.Users
                .SingleOrDefaultAsync(x => x.Id == _userId);

            if (user is null ||
                !user.IsActive ||
                (user.Role != UserRole.Administrator &&
                 user.Role != UserRole.EvidenceCustodian))
            {
                ShowStatus(
                    "You do not have permission to add locations.",
                    true);

                return;
            }

            if (parent.Id is Guid parentId &&
                !await database.StorageLocations.AnyAsync(
                    x => x.Id == parentId && x.IsActive))
            {
                ShowStatus(
                    "The parent location is no longer available. " +
                    "Reopen this screen.",
                    true);

                return;
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
                IsActive = true
            };

            database.StorageLocations.Add(location);

            database.AuditLogEntries.Add(new AuditLogEntry
            {
                UserId = _userId,
                Action = "StorageLocation.Created",
                EntityType = nameof(StorageLocation),
                EntityId = location.Id,
                Details = $"Storage location created: {name}."
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
            SaveButton.IsEnabled = true;
            FormPanel.IsEnabled = true;
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
            ShowStatus(
                "Location saved, but the table could not refresh. " +
                "Reopen this screen.",
                true);
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(255, 155, 155))
            : new SolidColorBrush(Color.FromRgb(134, 239, 172));

        StatusText.Visibility = Visibility.Visible;
    }

    private sealed class ParentChoice
    {
        public Guid? Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}