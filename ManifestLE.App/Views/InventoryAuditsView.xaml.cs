using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.App.Views;

public partial class InventoryAuditsView : UserControl
{
    private readonly Guid _userId;
    private readonly InventoryAuditService _service = new();

    private bool _initialized;
    private bool _busy;

    private Guid? _auditId;
    private bool _auditOpen;

    public InventoryAuditsView()
        : this(Guid.Empty)
    {
    }

    public InventoryAuditsView(Guid userId)
    {
        InitializeComponent();

        _userId = userId;

        ResultBox.ItemsSource =
            Enum.GetValues<InventoryAuditResult>();

        _initialized = true;

        Loaded += View_Loaded;
    }

    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= View_Loaded;

        await RunAsync(async () =>
        {
            await LoadListsAsync();
            StatusText.Text = "Select a storage location or an existing audit.";
        });
    }

    private async void RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            await LoadListsAsync(_auditId);
            StatusText.Text = "Audit records refreshed.";
        });
    }

    private async void StartButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (LocationBox.SelectedItem is not LocationChoice location)
        {
            StatusText.Text = "Select a storage location first.";
            return;
        }

        await RunAsync(async () =>
        {
            Guid id = await _service.StartAsync(
                _userId,
                location.Id,
                IncludeChildrenBox.IsChecked == true,
                StartNotesBox.Text);

            StartNotesBox.Clear();
            CompletionNotesBox.Clear();

            await LoadListsAsync(id);

            StatusText.Text =
                "Audit started. Select each item and save its inspection result.";
        });
    }

    private async void AuditBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_initialized || _busy)
        {
            return;
        }

        if (AuditBox.SelectedItem is not AuditChoice choice)
        {
            ClearAudit();
            return;
        }

        await RunAsync(async () =>
        {
            CompletionNotesBox.Clear();
            await LoadAuditAsync(choice.Id);
            StatusText.Text = "Audit loaded.";
        });
    }

    private async Task LoadListsAsync(Guid? selectedAuditId = null)
    {
        await using var database = DevelopmentDatabase.CreateContext();

        await RequireAccessAsync(database);

        Guid? previousLocation =
            (LocationBox.SelectedItem as LocationChoice)?.Id;

        var locations = await database.StorageLocations
            .AsNoTracking()
            .ToListAsync();

        var lookup = locations.ToDictionary(x => x.Id);

        var locationChoices = locations
            .Where(x => x.IsActive)
            .Select(x => new LocationChoice(
                x.Id,
                BuildPath(x.Id, lookup)))
            .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        LocationBox.ItemsSource = locationChoices;

        LocationBox.SelectedItem = locationChoices
            .FirstOrDefault(x => x.Id == previousLocation);

        var audits = await database.InventoryAudits
            .AsNoTracking()
            .ToListAsync();

        var choices = audits
            .OrderByDescending(x => x.StartedAt)
            .Select(x => new AuditChoice(
                x.Id,
                $"{FormatTime(x.StartedAt)} | " +
                $"{x.StoragePathSnapshot} | " +
                (x.Status == InventoryAuditStatus.InProgress
                    ? "In progress"
                    : "Completed")))
            .ToList();

        AuditBox.ItemsSource = choices;

        var selected = choices.FirstOrDefault(x =>
            x.Id == selectedAuditId);

        AuditBox.SelectedItem = selected;

        if (selected is null)
        {
            ClearAudit();
        }
        else
        {
            await LoadAuditAsync(selected.Id);
        }
    }

    private async Task LoadAuditAsync(Guid id)
    {
        ClearAudit();

        await using var database = DevelopmentDatabase.CreateContext();

        await RequireAccessAsync(database);

        var audit = await database.InventoryAudits
            .AsNoTracking()
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id);

        if (audit is null)
        {
            throw new InvalidOperationException(
                "The selected audit could not be found.");
        }

        var names = await database.Users
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName);

        _auditId = audit.Id;
        _auditOpen = audit.Status == InventoryAuditStatus.InProgress;

        int pending = audit.Items.Count(x =>
            x.Result == InventoryAuditResult.Pending);

        int verified = audit.Items.Count(x =>
            x.Result == InventoryAuditResult.Verified);

        int missing = audit.Items.Count(x =>
            x.Result == InventoryAuditResult.Missing);

        int discrepancies = audit.Items.Count(x =>
            x.Result == InventoryAuditResult.Discrepancy);

        string startedBy = names.GetValueOrDefault(
            audit.StartedByUserId,
            "Unknown user");

        AuditSummaryText.Text =
            $"{audit.StoragePathSnapshot}\n" +
            $"Started: {FormatTime(audit.StartedAt)} by {startedBy}\n" +
            $"Status: {(_auditOpen ? "In progress" : "Completed — locked")}\n" +
            $"Total: {audit.Items.Count} | Pending: {pending} | " +
            $"Verified: {verified} | Missing: {missing} | " +
            $"Discrepancies: {discrepancies}";

        StartingNotesText.Text =
            $"Starting notes: " +
            (string.IsNullOrWhiteSpace(audit.Notes)
                ? "None"
                : audit.Notes);

        var rows = audit.Items
            .OrderBy(x => x.ReportNumberSnapshot,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => NumericItemNumber(x.ItemNumberSnapshot))
            .ThenBy(x => x.ItemNumberSnapshot,
                StringComparer.OrdinalIgnoreCase)
            .Select(x => new ItemRow
            {
                Id = x.Id,
                Register = x.EvidenceItemId.HasValue
                    ? "Evidence"
                    : x.FoundPropertyItemId.HasValue
                        ? "Found Property"
                        : "Safekeeping",
                ReportNumber = x.ReportNumberSnapshot,
                ItemNumber = x.ItemNumberSnapshot,
                Description = x.DescriptionSnapshot,
                Storage = x.ExpectedStoragePathSnapshot,
                Result = x.Result,
                Notes = x.Notes,
                CheckedAt = x.CheckedAt,
                CheckedBy = x.CheckedByUserId is Guid userId
                    ? names.GetValueOrDefault(userId, "Unknown user")
                    : ""
            })
            .ToList();

        ItemsGrid.ItemsSource = rows;

        CompletionPanel.IsEnabled = _auditOpen;

        if (!_auditOpen)
        {
            var completion = await database.AuditLogEntries
                .AsNoTracking()
                .Where(x =>
                    x.EntityId == audit.Id &&
                    x.EntityType == nameof(InventoryAudit) &&
                    x.Action == "InventoryAudit.Completed")
                .ToListAsync();

            string completedBy = audit.CompletedByUserId is Guid userId
                ? names.GetValueOrDefault(userId, "Unknown user")
                : "Unknown user";

            CompletionDetailsBox.Text =
                $"Completed: " +
                (audit.CompletedAt is DateTimeOffset completedAt
                    ? FormatTime(completedAt)
                    : "Not recorded") +
                $" by {completedBy}\n\n" +
                (completion
                    .OrderByDescending(x => x.RecordedAt)
                    .FirstOrDefault()?.Details ?? "");

            CompletionDetailsBox.Visibility = Visibility.Visible;
        }
    }

    private void ItemsGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        if (ItemsGrid.SelectedItem is not ItemRow row)
        {
            ResetItemEditor();
            return;
        }

        SelectedItemText.Text =
            $"{row.Register} | {row.ReportNumber} | " +
            $"Item {row.ItemNumber}\n{row.Description}";

        ResultBox.SelectedItem = row.Result;
        ItemNotesBox.Text = row.Notes;
        ItemEditor.IsEnabled = _auditOpen;

        CheckInfoText.Text = row.CheckedAt is DateTimeOffset checkedAt
            ? $"Last check: {FormatTime(checkedAt)} by {row.CheckedBy}"
            : "This item has not been checked.";
    }

    private async void SaveItemButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_auditId is not Guid auditId ||
            ItemsGrid.SelectedItem is not ItemRow row ||
            ResultBox.SelectedItem is not InventoryAuditResult result)
        {
            StatusText.Text = "Select an audit item and inspection result.";
            return;
        }

        string notes = ItemNotesBox.Text;

        await RunAsync(async () =>
        {
            await _service.RecordResultAsync(
                _userId,
                auditId,
                row.Id,
                result,
                notes,
                row.Result,
                row.CheckedAt);

            await LoadAuditAsync(auditId);

            var rows = ItemsGrid.ItemsSource as List<ItemRow>;

            ItemsGrid.SelectedItem = rows?.FirstOrDefault(x =>
                x.Id == row.Id);

            StatusText.Text = "Item inspection saved.";
        });
    }

    private async void CompleteButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_auditId is not Guid auditId || !_auditOpen)
        {
            return;
        }

        var answer = MessageBox.Show(
            Window.GetWindow(this),
            "Complete this audit and lock its results?\n\n" +
            "Saved missing items and discrepancies will remain in the report.",
            "Complete Inventory Audit",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        string notes = CompletionNotesBox.Text;

        await RunAsync(async () =>
        {
            await _service.CompleteAsync(
                _userId,
                auditId,
                notes);

            CompletionNotesBox.Clear();

            await LoadListsAsync(auditId);

            StatusText.Text = "Audit completed. Its results are now locked.";
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        ScreenPanel.IsEnabled = false;

        try
        {
            await action();
        }
        catch (UnauthorizedAccessException ex)
        {
            ClearAudit();
            AuditBox.ItemsSource = null;
            LocationBox.ItemsSource = null;
            StatusText.Text = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
        catch (Exception)
        {
            StatusText.Text =
                "The operation could not be completed. " +
                "Refresh before trying again.";
        }
        finally
        {
            ScreenPanel.IsEnabled = true;
            _busy = false;
        }
    }

    private async Task RequireAccessAsync(ManifestDbContext database)
    {
        var user = await database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == _userId);

        if (user is null ||
            !user.IsActive ||
            (user.Role != UserRole.Administrator &&
             user.Role != UserRole.EvidenceCustodian))
        {
            throw new UnauthorizedAccessException(
                "Inventory audits require an active administrator " +
                "or evidence custodian account.");
        }
    }

    private void ClearAudit()
    {
        _auditId = null;
        _auditOpen = false;

        ItemsGrid.ItemsSource = null;
        AuditSummaryText.Text = "Select an audit.";
        StartingNotesText.Text = "";

        CompletionPanel.IsEnabled = false;
        CompletionDetailsBox.Text = "";
        CompletionDetailsBox.Visibility = Visibility.Collapsed;

        ResetItemEditor();
    }

    private void ResetItemEditor()
    {
        ItemEditor.IsEnabled = false;
        ResultBox.SelectedItem = null;
        ItemNotesBox.Clear();

        SelectedItemText.Text =
            "Select an item to record its inspection.";

        CheckInfoText.Text = "";
    }

    private static long NumericItemNumber(string value)
    {
        return long.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out long number)
                ? number
                : long.MaxValue;
    }

    private static string FormatTime(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString(
            "yyyy-MM-dd HH:mm zzz",
            CultureInfo.InvariantCulture);
    }

    private static string BuildPath(
        Guid locationId,
        IReadOnlyDictionary<Guid, StorageLocation> locations)
    {
        var parts = new List<string>();
        var visited = new HashSet<Guid>();

        Guid? current = locationId;

        while (current is Guid id)
        {
            if (!visited.Add(id))
            {
                parts.Add("[Location cycle]");
                break;
            }

            if (!locations.TryGetValue(id, out var location))
            {
                parts.Add("[Location unavailable]");
                break;
            }

            parts.Add(location.IsActive
                ? location.Name
                : $"{location.Name} (inactive)");

            current = location.ParentLocationId;
        }

        parts.Reverse();

        return string.Join(" / ", parts);
    }

    public sealed record LocationChoice(Guid Id, string Label);

    public sealed record AuditChoice(Guid Id, string Label);

    public sealed class ItemRow
    {
        public Guid Id { get; set; }

        public string Register { get; set; } = "";

        public string ReportNumber { get; set; } = "";

        public string ItemNumber { get; set; } = "";

        public string Description { get; set; } = "";

        public string Storage { get; set; } = "";

        public InventoryAuditResult Result { get; set; }

        public string Notes { get; set; } = "";

        public DateTimeOffset? CheckedAt { get; set; }

        public string CheckedBy { get; set; } = "";
    }
}