using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.App.Views;

public partial class AuditHistoryView : UserControl
{
    private const string SelectionPrompt =
        "Select an audit entry to view its details.";

    private readonly Guid _userId;
    private List<AuditRow> _rows = new();

    private bool _initialized;
    private bool _isLoading;
    private bool _hasAccess;
    private Guid? _timelineAuditId;

    public AuditHistoryView()
        : this(Guid.Empty)
    {
    }

    public AuditHistoryView(Guid userId)
    {
        InitializeComponent();

        _userId = userId;
        AllButton.IsChecked = true;
        _initialized = true;

        Loaded += View_Loaded;
    }

    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= View_Loaded;
        await LoadHistoryAsync();
    }

    private async void RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        _hasAccess = false;

        RefreshButton.IsEnabled = false;
        ClearFiltersButton.IsEnabled = false;
        TimelineButton.IsEnabled = false;

        _rows.Clear();
        AuditGrid.ItemsSource = null;
        DetailsBox.Text = SelectionPrompt;
        StatusText.Text = "Loading audit history…";

        try
        {
            await using var database =
                DevelopmentDatabase.CreateContext();

            var user = await database.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == _userId);

            if (user is null ||
                !user.IsActive ||
                (user.Role != UserRole.Administrator &&
                 user.Role != UserRole.EvidenceCustodian))
            {
                DetailsBox.Text = "Access denied.";
                StatusText.Text =
                    "Audit History requires an active administrator " +
                    "or evidence custodian account.";
                return;
            }

            var names = await database.Users
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName);

            var entries = await database.AuditLogEntries
                .AsNoTracking()
                .ToListAsync();

            var properties =
                new Dictionary<(string Type, Guid Id), PropertyReference>();

            var evidence = await database.EvidenceItems
                .AsNoTracking()
                .Select(x => new
                {
                    x.Id,
                    x.CaseNumber,
                    x.ItemNumber,
                    x.Description
                })
                .ToListAsync();

            foreach (var item in evidence)
            {
                properties[("EvidenceItem", item.Id)] =
                    new PropertyReference(
                        item.CaseNumber,
                        item.ItemNumber,
                        item.Description,
                        false);
            }

            var found = await database.FoundPropertyItems
                .AsNoTracking()
                .Select(x => new
                {
                    x.Id,
                    x.ReportNumber,
                    x.ItemNumber,
                    x.Description
                })
                .ToListAsync();

            foreach (var item in found)
            {
                properties[("FoundPropertyItem", item.Id)] =
                    new PropertyReference(
                        item.ReportNumber,
                        item.ItemNumber,
                        item.Description,
                        false);
            }

            var safekeeping = await database.SafekeepingItems
                .AsNoTracking()
                .Select(x => new
                {
                    x.Id,
                    x.ReportNumber,
                    x.ItemNumber,
                    x.Description
                })
                .ToListAsync();

            foreach (var item in safekeeping)
            {
                properties[("SafekeepingItem", item.Id)] =
                    new PropertyReference(
                        item.ReportNumber,
                        item.ItemNumber,
                        item.Description,
                        false);
            }

            var events = await database.CustodyEvents
                .AsNoTracking()
                .Select(x => new
                {
                    x.Id,
                    x.EvidenceItemId,
                    x.FoundPropertyItemId,
                    x.SafekeepingItemId
                })
                .ToListAsync();

            foreach (var movement in events)
            {
                PropertyReference? reference = null;

                if (movement.EvidenceItemId is Guid evidenceId)
                {
                    properties.TryGetValue(
                        ("EvidenceItem", evidenceId),
                        out reference);
                }
                else if (movement.FoundPropertyItemId is Guid foundId)
                {
                    properties.TryGetValue(
                        ("FoundPropertyItem", foundId),
                        out reference);
                }
                else if (movement.SafekeepingItemId is Guid safeId)
                {
                    properties.TryGetValue(
                        ("SafekeepingItem", safeId),
                        out reference);
                }

                if (reference is not null)
                {
                    properties[("CustodyEvent", movement.Id)] = reference;
                }
            }

            var audits = await database.InventoryAudits
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Id);

            var auditItems = await database.InventoryAuditItems
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Id);

            foreach (var item in auditItems.Values)
            {
                properties[(nameof(InventoryAuditItem), item.Id)] =
                    new PropertyReference(
                        item.ReportNumberSnapshot,
                        item.ItemNumberSnapshot,
                        item.DescriptionSnapshot,
                        true);
            }

            _rows = entries.Select(entry =>
            {
                PropertyReference? property = null;
                Guid? inventoryAuditId = null;
                string inventoryStorage = "";

                if (entry.EntityId is Guid entityId)
                {
                    properties.TryGetValue(
                        (entry.EntityType, entityId),
                        out property);

                    if (entry.EntityType == nameof(InventoryAudit) &&
                        audits.ContainsKey(entityId))
                    {
                        inventoryAuditId = entityId;
                    }
                    else if (
                        entry.EntityType == nameof(InventoryAuditItem) &&
                        auditItems.TryGetValue(entityId, out var auditItem))
                    {
                        inventoryAuditId = auditItem.InventoryAuditId;
                    }
                }

                if (inventoryAuditId is Guid auditId &&
                    audits.TryGetValue(auditId, out var audit))
                {
                    inventoryStorage = audit.StoragePathSnapshot;
                }

                return new AuditRow
                {
                    Id = entry.Id,
                    RecordedAt = entry.RecordedAt,
                    UserId = entry.UserId,
                    UserName = entry.UserId is Guid userId
                        ? names.GetValueOrDefault(userId, "Unknown user")
                        : "System / unauthenticated",
                    RawAction = entry.Action,
                    Action = ReadableAction(entry.Action),
                    EntityType = entry.EntityType,
                    RecordType = ReadableRecordType(entry.EntityType),
                    EntityId = entry.EntityId,
                    Details = entry.Details,
                    CaseNumber = property?.CaseNumber ?? "",
                    ItemNumber = property?.ItemNumber ?? "",
                    Description = property?.Description ?? "",
                    UsesSnapshot = property?.IsSnapshot ?? false,
                    InventoryAuditId = inventoryAuditId,
                    InventoryStorage = inventoryStorage
                };
            }).ToList();

            _hasAccess = true;
            ApplyFilters();
        }
        catch (Exception)
        {
            _hasAccess = false;
            _rows.Clear();
            AuditGrid.ItemsSource = null;
            DetailsBox.Text = SelectionPrompt;

            StatusText.Text =
                "Audit history could not be loaded. " +
                "Select Refresh to try again.";
        }
        finally
        {
            _isLoading = false;
            RefreshButton.IsEnabled = true;
            ClearFiltersButton.IsEnabled = true;
        }
    }

    private void SearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        RefreshFilters();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        RefreshFilters();
    }

    private void RefreshFilters()
    {
        if (_initialized && !_isLoading && _hasAccess)
        {
            ApplyFilters();
        }
    }

    private void ClearFiltersButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _initialized = false;

        _timelineAuditId = null;
        SearchBox.Clear();
        AllButton.IsChecked = true;
        OldestFirstBox.IsChecked = false;

        _initialized = true;
        RefreshFilters();

        SearchBox.Focus();
    }

    private void TimelineButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (AuditGrid.SelectedItem is not AuditRow row ||
            row.InventoryAuditId is not Guid auditId)
        {
            return;
        }

        _initialized = false;

        _timelineAuditId = auditId;
        SearchBox.Clear();
        InventoryButton.IsChecked = true;
        OldestFirstBox.IsChecked = true;

        _initialized = true;
        RefreshFilters();
    }

    private void ApplyFilters()
    {
        string search = SearchBox.Text.Trim();

        string[] terms = search.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries);

        var matches = _rows.Where(row =>
            MatchesCategory(row) &&
            (!_timelineAuditId.HasValue ||
             row.InventoryAuditId == _timelineAuditId) &&
            terms.All(term => MatchesSearch(row, term)));

        bool oldestFirst = OldestFirstBox.IsChecked == true;

        var filtered = oldestFirst
            ? matches.OrderBy(x => x.RecordedAt)
                .ThenBy(x => x.Id).ToList()
            : matches.OrderByDescending(x => x.RecordedAt)
                .ThenByDescending(x => x.Id).ToList();

        AuditGrid.ItemsSource = filtered;
        TimelineButton.IsEnabled = false;

        DetailsBox.Text = filtered.Count == 0
            ? "No audit entries match the current filters.\n\n" +
              "Use Clear filters to show all loaded activity."
            : SelectionPrompt;

        TimelineText.Visibility = _timelineAuditId.HasValue
            ? Visibility.Visible
            : Visibility.Collapsed;

        TimelineText.Text =
            $"Viewing inventory audit: {_timelineAuditId}. " +
            "Clear filters returns to all activity.";

        string searchDescription = search.Length == 0
            ? "No search text"
            : $"Search: \"{search}\"";

        StatusText.Text =
            $"{filtered.Count} matching entries of {_rows.Count}. " +
            $"Filter: {SelectedCategory()}. " +
            $"{(oldestFirst ? "Oldest first" : "Newest first")}. " +
            $"{searchDescription}.";
    }

    private static bool MatchesSearch(AuditRow row, string search)
    {
        string[] values =
        {
            row.CaseNumber,
            row.ItemNumber,
            row.Description,
            row.UserName,
            row.Action,
            row.RawAction,
            row.EntityType,
            row.RecordType,
            row.EntityId?.ToString() ?? "",
            row.UserId?.ToString() ?? "",
            row.Id.ToString(),
            row.InventoryAuditId?.ToString() ?? "",
            row.InventoryStorage,
            row.Details
        };

        return values.Any(value => value.Contains(
            search,
            StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesCategory(AuditRow row)
    {
        string action = row.RawAction;

        if (AuthenticationButton.IsChecked == true)
        {
            return action.StartsWith(
                "Authentication.",
                StringComparison.OrdinalIgnoreCase);
        }

        if (IntakeButton.IsChecked == true)
        {
            return action.EndsWith(
                       ".Intake",
                       StringComparison.OrdinalIgnoreCase) ||
                   action.Equals(
                       "Evidence.Created",
                       StringComparison.OrdinalIgnoreCase);
        }

        if (CustodyButton.IsChecked == true)
        {
            return action.StartsWith(
                "Custody.",
                StringComparison.OrdinalIgnoreCase);
        }

        if (InventoryButton.IsChecked == true)
        {
            return action.StartsWith(
                "InventoryAudit.",
                StringComparison.OrdinalIgnoreCase);
        }

        if (SetupButton.IsChecked == true)
        {
            return action.StartsWith(
                       "Agency.",
                       StringComparison.OrdinalIgnoreCase) ||
                   action.StartsWith(
                       "User.",
                       StringComparison.OrdinalIgnoreCase) ||
                   action.StartsWith(
                       "StorageLocation.",
                       StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private string SelectedCategory()
    {
        if (AuthenticationButton.IsChecked == true)
            return "Authentication";

        if (IntakeButton.IsChecked == true)
            return "Intake";

        if (CustodyButton.IsChecked == true)
            return "Custody";

        if (InventoryButton.IsChecked == true)
            return "Inventory audits";

        if (SetupButton.IsChecked == true)
            return "Setup / storage";

        return "All activity";
    }

    private static string ReadableAction(string value)
    {
        string[] parts = value.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return "Unknown Action";
        }

        string category = parts.Length > 1 ? parts[0] : "";
        string operation = parts[^1];

        if (category == "InventoryAudit")
        {
            return operation switch
            {
                "Started" => "Inventory Audit Started",
                "ItemChecked" => "Inventory Item Checked",
                "Completed" => "Inventory Audit Completed",
                _ => $"Inventory Audit {SpaceWords(operation)}"
            };
        }

        if (operation.Equals(
                "Intake",
                StringComparison.OrdinalIgnoreCase) ||
            value.Equals(
                "Evidence.Created",
                StringComparison.OrdinalIgnoreCase))
        {
            return category switch
            {
                "Evidence" or "EvidenceItem" => "Evidence Intake",
                "FoundProperty" or "FoundPropertyItem" =>
                    "Found Property Intake",
                "Safekeeping" or "SafekeepingItem" =>
                    "Safekeeping Intake",
                _ => "Intake"
            };
        }

        string label = operation switch
        {
            "CheckIn" => "Check In",
            "CheckOut" => "Check Out",
            "Transfer" or "StorageTransfer" => "Storage Transfer",
            "SignedIn" => "Signed In",
            "SignedOut" => "Signed Out",
            "PasswordVerified" => "Password Verified",
            "MfaEnrolled" or "MFAEnrolled" => "MFA Enrolled",
            "MfaVerified" or "MFAVerified" => "MFA Verified",
            "MfaFailed" or "MFAFailed" => "MFA Failed",
            "MfaRejected" or "MFARejected" => "MFA Rejected",
            _ => SpaceWords(operation)
        };

        return category switch
        {
            "Agency" => $"Agency {label}",
            "User" or "AppUser" => $"User {label}",
            "StorageLocation" => $"Storage Location {label}",
            _ => label
        };
    }

    private static string ReadableRecordType(string value)
    {
        return value switch
        {
            "AppUser" => "User",
            "UserCredential" => "User Credential",
            "EvidenceItem" => "Evidence Item",
            "FoundPropertyItem" => "Found Property Item",
            "SafekeepingItem" => "Safekeeping Item",
            "StorageLocation" => "Storage Location",
            "CustodyEvent" => "Custody Event",
            "AuditLogEntry" => "Audit Log Entry",
            "InventoryAudit" => "Inventory Audit",
            "InventoryAuditItem" => "Inventory Audit Item",
            _ => SpaceWords(value)
        };
    }

    private static string SpaceWords(string value)
    {
        string spaced = Regex.Replace(
            value,
            @"([A-Z]+)([A-Z][a-z])",
            "$1 $2");

        spaced = Regex.Replace(
            spaced,
            @"([a-z0-9])([A-Z])",
            "$1 $2");

        return spaced.Replace('_', ' ').Replace('.', ' ').Trim();
    }

    private void AuditGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (DetailsBox is null || TimelineButton is null)
        {
            return;
        }

        if (AuditGrid.SelectedItem is not AuditRow row)
        {
            TimelineButton.IsEnabled = false;
            DetailsBox.Text = SelectionPrompt;
            return;
        }

        TimelineButton.IsEnabled =
            _hasAccess && row.InventoryAuditId.HasValue;

        string propertyDetails = row.CaseNumber.Length == 0
            ? ""
            : $"Case / report: {row.CaseNumber}\n\n" +
              $"Item number: {row.ItemNumber}\n\n" +
              $"Description: {row.Description}\n\n";

        string inventoryDetails = row.InventoryAuditId.HasValue
            ? $"Inventory audit ID: {row.InventoryAuditId}\n\n" +
              $"Audit storage: {row.InventoryStorage}\n\n"
            : "";

        string referenceNote = row.UsesSnapshot
            ? "Case, item, and description are from the inventory " +
              "audit snapshot. User display name reflects the current record."
            : "Linked case, item, description, and user display name " +
              "reflect current records.";

        DetailsBox.Text =
            $"Recorded locally: {row.RecordedLocal}\n\n" +
            $"Recorded UTC: " +
            $"{row.RecordedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC\n\n" +
            $"User: {row.UserName}\n\n" +
            $"Action: {row.Action}\n\n" +
            $"Record type: {row.RecordType}\n\n" +
            propertyDetails +
            inventoryDetails +
            $"Recorded details: {row.Details}\n\n" +
            $"Record ID: {row.EntityId?.ToString() ?? "Not associated"}\n\n" +
            $"User ID: {row.UserId?.ToString() ?? "Not associated"}\n\n" +
            $"Audit entry ID: {row.Id}\n\n" +
            referenceNote + " Recorded details are unchanged.";
    }

    private sealed record PropertyReference(
        string CaseNumber,
        string ItemNumber,
        string Description,
        bool IsSnapshot);

    public sealed class AuditRow
    {
        public Guid Id { get; set; }
        public DateTimeOffset RecordedAt { get; set; }
        public Guid? UserId { get; set; }
        public string UserName { get; set; } = "";
        public string RawAction { get; set; } = "";
        public string Action { get; set; } = "";
        public string EntityType { get; set; } = "";
        public string RecordType { get; set; } = "";
        public Guid? EntityId { get; set; }
        public string CaseNumber { get; set; } = "";
        public string ItemNumber { get; set; } = "";
        public string Description { get; set; } = "";
        public string Details { get; set; } = "";
        public bool UsesSnapshot { get; set; }
        public Guid? InventoryAuditId { get; set; }
        public string InventoryStorage { get; set; } = "";

        public string RecordedLocal =>
            RecordedAt.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss zzz",
                CultureInfo.InvariantCulture);
    }
}