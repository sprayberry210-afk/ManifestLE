using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.App.Views;

public partial class EvidenceIntakeView : UserControl
{
    private const string DateFormat = "yyyy-MM-dd HH:mm zzz";

    private readonly Guid _userId;
    private bool _isSaving;
    private string _defaultCollector = string.Empty;

    public EvidenceIntakeView()
        : this(Guid.Empty)
    {
    }

    public EvidenceIntakeView(Guid userId)
    {
        InitializeComponent();

        _userId = userId;

        RecordTypeBox.ItemsSource = new[]
        {
            "Evidence",
            "Found Property",
            "Safekeeping"
        };

        PackagingBox.ItemsSource = new[]
        {
            "Paper bag",
            "Evidence envelope",
            "Evidence box",
            "Plastic bag",
            "Sharps container",
            "Firearm box",
            "Other / see notes",
            "Unpackaged / see notes"
        };

        RecordTypeBox.SelectedIndex = 0;
        SetCollectionTime();

        Loaded += EvidenceIntakeView_Loaded;
    }

    private async void EvidenceIntakeView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= EvidenceIntakeView_Loaded;

        try
        {
            await using var database =
                DevelopmentDatabase.CreateContext();

            var user = await database.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == _userId);

            if (!CanIntake(user))
            {
                FormPanel.IsEnabled = false;
                ShowStatus("You do not have permission to enter items.", true);
                return;
            }

            _defaultCollector = user!.DisplayName;
            CollectedByBox.Text = _defaultCollector;

            var locations = await database.StorageLocations
                .AsNoTracking()
                .ToListAsync();

            StorageBox.ItemsSource = locations
                .Where(x => x.IsActive)
                .Select(x => new LocationChoice
                {
                    Id = x.Id,
                    Name = BuildPath(x, locations)
                })
                .OrderBy(x => x.Name)
                .ToList();

            if (!locations.Any(x => x.IsActive))
            {
                ShowStatus("Add an active storage location before intake.", true);
                return;
            }

            SaveButton.IsEnabled = true;
        }
        catch (Exception)
        {
            FormPanel.IsEnabled = false;
            SaveButton.IsEnabled = false;
            ShowStatus("Intake could not be initialized.", true);
        }
    }

    private static bool CanIntake(AppUser? user)
    {
        return user is not null &&
               user.IsActive &&
               (user.Role == UserRole.Officer ||
                user.Role == UserRole.EvidenceCustodian ||
                user.Role == UserRole.Administrator);
    }

    private static string BuildPath(
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

    private void RecordTypeBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (FoundPropertyPanel is null || SafekeepingPanel is null)
        {
            return;
        }

        FoundPropertyPanel.Visibility =
            RecordTypeBox.SelectedIndex == 1
                ? Visibility.Visible
                : Visibility.Collapsed;

        SafekeepingPanel.Visibility =
            RecordTypeBox.SelectedIndex == 2
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void IsFirearmBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (FirearmPanel is not null)
        {
            FirearmPanel.Visibility = IsFirearmBox.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void SetCollectionTime()
    {
        CollectedAtBox.Text = DateTimeOffset.Now.ToString(
            DateFormat,
            CultureInfo.InvariantCulture);
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

        int type = RecordTypeBox.SelectedIndex;

        string report = ReportNumberBox.Text.Trim().ToUpperInvariant();
        string itemNumber = ItemNumberBox.Text.Trim().ToUpperInvariant();
        string description = DescriptionBox.Text.Trim();
        string collector = CollectedByBox.Text.Trim();
        string collectionLocation = CollectionLocationBox.Text.Trim();
        string reason = CustodyReasonBox.Text.Trim();
        string notes = NotesBox.Text.Trim();

        if (type < 0 || type > 2)
        {
            ShowStatus("Choose a record type.", true);
            return;
        }

        if (string.IsNullOrWhiteSpace(report) ||
            string.IsNullOrWhiteSpace(itemNumber) ||
            string.IsNullOrWhiteSpace(description) ||
            string.IsNullOrWhiteSpace(collector) ||
            string.IsNullOrWhiteSpace(collectionLocation) ||
            string.IsNullOrWhiteSpace(reason))
        {
            ShowStatus("Complete all required fields marked with *.", true);
            return;
        }

        if (!DateTimeOffset.TryParseExact(
            CollectedAtBox.Text.Trim(),
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTimeOffset collectedAt))
        {
            ShowStatus(
                "Enter collection time as yyyy-MM-dd HH:mm " +
                "followed by its UTC offset, such as -05:00.",
                true);

            return;
        }

        if (collectedAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            ShowStatus("Collection time cannot be in the future.", true);
            return;
        }

        if (PackagingBox.SelectedItem is not string packaging ||
            StorageBox.SelectedItem is not LocationChoice storage)
        {
            ShowStatus("Choose packaging and a storage location.", true);
            return;
        }

        if ((packaging == "Other / see notes" ||
             packaging == "Unpackaged / see notes") &&
            string.IsNullOrWhiteSpace(notes))
        {
            ShowStatus("Describe the packaging circumstances in notes.", true);
            return;
        }

        if (type == 2 &&
            (string.IsNullOrWhiteSpace(HoldReasonBox.Text) ||
             string.IsNullOrWhiteSpace(HoldAuthorityBox.Text)))
        {
            ShowStatus(
                "Enter the safekeeping reason and documented authority.",
                true);

            return;
        }

        _isSaving = true;
        SaveButton.IsEnabled = false;
        FormPanel.IsEnabled = false;

        bool saved = false;
        Guid recordId = Guid.NewGuid();
        string entityType = string.Empty;

        try
        {
            await using var database =
                DevelopmentDatabase.CreateContext();

            await using var transaction =
                await database.Database.BeginTransactionAsync();

            var user = await database.Users
                .SingleOrDefaultAsync(x => x.Id == _userId);

            if (!CanIntake(user))
            {
                ShowStatus("Your account cannot enter items.", true);
                return;
            }

            if (!await database.StorageLocations.AnyAsync(
                x => x.Id == storage.Id && x.IsActive))
            {
                ShowStatus(
                    "The selected storage location is unavailable. " +
                    "Reopen intake.",
                    true);

                return;
            }

            bool duplicate = type switch
            {
                0 => await database.EvidenceItems.AnyAsync(
                    x => x.CaseNumber == report &&
                         x.ItemNumber == itemNumber),

                1 => await database.FoundPropertyItems.AnyAsync(
                    x => x.ReportNumber == report &&
                         x.ItemNumber == itemNumber),

                2 => await database.SafekeepingItems.AnyAsync(
                    x => x.ReportNumber == report &&
                         x.ItemNumber == itemNumber),

                _ => true
            };

            if (duplicate)
            {
                ShowStatus(
                    "That case/report and item number already exist " +
                    "in the selected register.",
                    true);

                return;
            }

            var custody = new CustodyEvent
            {
                EventType = CustodyEventType.Intake,
                OccurredAt = DateTimeOffset.UtcNow,
                RecordedByUserId = _userId,
                FromCustodian = collector,
                ToCustodian = user!.DisplayName,
                ToStorageLocationId = storage.Id,
                Reason = reason
            };

            string ownerName = OwnerNameBox.Text.Trim();
            string ownerContact = OwnerContactBox.Text.Trim();

            if (type == 0)
            {
                entityType = nameof(EvidenceItem);

                database.EvidenceItems.Add(new EvidenceItem
                {
                    Id = recordId,
                    CaseNumber = report,
                    ItemNumber = itemNumber,
                    Description = description,
                    CollectedBy = collector,
                    CollectedAt = collectedAt,
                    CollectionLocation = collectionLocation,
                    Packaging = packaging,
                    OwnerName = ownerName,
                    OwnerContact = ownerContact,
                    Notes = notes,
                    CurrentStorageLocationId = storage.Id
                });

                custody.EvidenceItemId = recordId;
            }
            else if (type == 1)
            {
                entityType = nameof(FoundPropertyItem);

                database.FoundPropertyItems.Add(new FoundPropertyItem
                {
                    Id = recordId,
                    ReportNumber = report,
                    ItemNumber = itemNumber,
                    Description = description,
                    CollectedBy = collector,
                    CollectedAt = collectedAt,
                    FoundLocation = collectionLocation,
                    Packaging = packaging,
                    OwnerName = ownerName,
                    OwnerContact = ownerContact,
                    FinderName = FinderNameBox.Text.Trim(),
                    FinderContact = FinderContactBox.Text.Trim(),
                    Notes = notes,
                    CurrentStorageLocationId = storage.Id
                });

                custody.FoundPropertyItemId = recordId;
            }
            else
            {
                entityType = nameof(SafekeepingItem);
                bool isFirearm = IsFirearmBox.IsChecked == true;

                database.SafekeepingItems.Add(new SafekeepingItem
                {
                    Id = recordId,
                    ReportNumber = report,
                    ItemNumber = itemNumber,
                    Description = description,
                    CollectedBy = collector,
                    CollectedAt = collectedAt,
                    CollectionLocation = collectionLocation,
                    Packaging = packaging,
                    OwnerName = ownerName,
                    OwnerContact = ownerContact,
                    HoldReason = HoldReasonBox.Text.Trim(),
                    HoldAuthority = HoldAuthorityBox.Text.Trim(),
                    IsFirearm = isFirearm,
                    Manufacturer = isFirearm
                        ? ManufacturerBox.Text.Trim() : string.Empty,
                    Model = isFirearm
                        ? ModelBox.Text.Trim() : string.Empty,
                    SerialNumber = isFirearm
                        ? SerialNumberBox.Text.Trim() : string.Empty,
                    Caliber = isFirearm
                        ? CaliberBox.Text.Trim() : string.Empty,
                    Notes = notes,
                    CurrentStorageLocationId = storage.Id
                });

                custody.SafekeepingItemId = recordId;
            }

            database.CustodyEvents.Add(custody);

            database.AuditLogEntries.Add(new AuditLogEntry
            {
                UserId = _userId,
                Action = $"{entityType}.Intake",
                EntityType = entityType,
                EntityId = recordId,
                Details =
                    $"Report {report}, item {itemNumber} entered into storage."
            });

            await database.SaveChangesAsync();
            await transaction.CommitAsync();

            saved = true;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqliteException sqlite &&
                  sqlite.SqliteExtendedErrorCode == 2067)
        {
            ShowStatus(
                "That case/report and item number already exist " +
                "in the selected register.",
                true);
        }
        catch (Exception)
        {
            ShowStatus(
                "Intake could not be saved. Your entries have been retained.",
                true);
        }
        finally
        {
            _isSaving = false;
            FormPanel.IsEnabled = true;
            SaveButton.IsEnabled = true;
        }

        if (saved)
        {
            ClearAfterSave();

            ShowStatus(
                $"Saved {report}, item {itemNumber}, " +
                "with its initial custody and audit records.",
                false);
        }
    }

    private void ClearAfterSave()
    {
        ReportNumberBox.Clear();
        ItemNumberBox.Clear();
        DescriptionBox.Clear();
        CollectionLocationBox.Clear();
        OwnerNameBox.Clear();
        OwnerContactBox.Clear();
        FinderNameBox.Clear();
        FinderContactBox.Clear();
        HoldReasonBox.Clear();
        HoldAuthorityBox.Clear();
        ManufacturerBox.Clear();
        ModelBox.Clear();
        SerialNumberBox.Clear();
        CaliberBox.Clear();
        CustodyReasonBox.Clear();
        NotesBox.Clear();

        IsFirearmBox.IsChecked = false;
        PackagingBox.SelectedIndex = -1;
        StorageBox.SelectedIndex = -1;
        CollectedByBox.Text = _defaultCollector;

        SetCollectionTime();
        ReportNumberBox.Focus();
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;

        StatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(255, 155, 155))
            : new SolidColorBrush(Color.FromRgb(134, 239, 172));

        StatusText.Visibility = Visibility.Visible;
    }

    private sealed class LocationChoice
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public override string ToString()
        {
            return Name;
        }
    }
}