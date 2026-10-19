using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.App.Views;

public partial class ChainOfCustodyView : UserControl
{
    private readonly Guid _userId;
    private readonly CustodyService _custodyService = new();
    private readonly StorageDisplayService _storageDisplayService = new();

    private bool _ready;
    private bool _busy;
    private bool _loadingItem;
    private bool _canManage;
    private bool _historyLoaded;
    private int _selectionVersion;

    private EvidenceStatus? _displayedStatus;
    private Guid? _displayedLocationId;

    private string _displayName = string.Empty;
    private string _lastRecipient = string.Empty;

    private IReadOnlyDictionary<Guid, string> _locationLabels =
        new Dictionary<Guid, string>();

    private IReadOnlyDictionary<Guid, string> _historyNames =
        new Dictionary<Guid, string>();

    private List<HistoryEntry> _historyEntries = new();

    public ChainOfCustodyView()
        : this(Guid.Empty)
    {
    }

    public ChainOfCustodyView(Guid userId)
    {
        InitializeComponent();

        _userId = userId;

        SelectionPanel.IsEnabled = false;
        SetMovementVisibility(false);

        RegisterBox.ItemsSource = new[]
        {
            new RegisterChoice
            {
                Type = PropertyRegisterKind.Evidence,
                Name = "Evidence"
            },
            new RegisterChoice
            {
                Type = PropertyRegisterKind.FoundProperty,
                Name = "Found Property"
            },
            new RegisterChoice
            {
                Type = PropertyRegisterKind.Safekeeping,
                Name = "Safekeeping"
            }
        };

        Loaded += View_Loaded;
    }

    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= View_Loaded;

        try
        {
            await using var database =
                DevelopmentDatabase.CreateContext();

            var user = await database.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == _userId);

            if (!IsActiveUser(user))
            {
                ShowStatus(
                    "Your account cannot access custody records.",
                    true);
                return;
            }

            _displayName = user!.DisplayName;
            _canManage = CanManage(user);
            _ready = true;

            SelectionPanel.IsEnabled = true;
            RegisterBox.SelectedIndex = 0;
        }
        catch (Exception)
        {
            ShowStatus(
                "Custody records could not be initialized.",
                true);
        }
    }

    private static bool IsActiveUser(AppUser? user)
    {
        return user is not null &&
               user.IsActive &&
               (user.Role == UserRole.Officer ||
                user.Role == UserRole.EvidenceCustodian ||
                user.Role == UserRole.Administrator);
    }

    private static bool CanManage(AppUser? user)
    {
        return IsActiveUser(user) &&
               (user!.Role == UserRole.Administrator ||
                user.Role == UserRole.EvidenceCustodian);
    }

    private void SetMovementVisibility(bool visible)
    {
        MovementPanel.Visibility = visible
            ? Visibility.Visible
            : Visibility.Collapsed;

        SaveButton.Visibility = visible
            ? Visibility.Visible
            : Visibility.Collapsed;

        MovementPanel.IsEnabled = false;
        SaveButton.IsEnabled = false;
    }

    private void ClearDisplayedItem()
    {
        SetMovementVisibility(false);

        _displayedStatus = null;
        _displayedLocationId = null;
        _lastRecipient = string.Empty;
        _historyLoaded = false;

        _locationLabels = new Dictionary<Guid, string>();
        _historyNames = new Dictionary<Guid, string>();
        _historyEntries.Clear();

        ActionBox.ItemsSource = null;
        DestinationBox.ItemsSource = null;

        FromCustodianBox.Clear();
        ToCustodianBox.Clear();
        ReasonBox.Clear();
        NotesBox.Clear();

        CurrentLocationText.Text = string.Empty;
        CurrentCustodianText.Text = string.Empty;
        ActionHelpText.Text = string.Empty;

        NewestFirstBox.IsEnabled = false;
        HistoryOrderText.Text = "Select an item to view its timeline.";
        HistoryBox.Text = "Select an item to view its custody history.";
    }

    private async void RegisterBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_ready || _busy ||
            RegisterBox.SelectedItem is not RegisterChoice register)
        {
            return;
        }

        ++_selectionVersion;
        _busy = true;

        SelectionPanel.IsEnabled = false;
        ClearDisplayedItem();

        ItemBox.ItemsSource = null;
        StatusText.Visibility = Visibility.Collapsed;

        try
        {
            await using var database =
                DevelopmentDatabase.CreateContext();

            var user = await database.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == _userId);

            if (!IsActiveUser(user))
            {
                _ready = false;
                _canManage = false;

                ShowStatus(
                    "Your account cannot access custody records.",
                    true);
                return;
            }

            _canManage = CanManage(user);

            List<ItemChoice> items;

            switch (register.Type)
            {
                case PropertyRegisterKind.Evidence:
                    items = await database.EvidenceItems
                        .AsNoTracking()
                        .Select(x => new ItemChoice
                        {
                            Id = x.Id,
                            Name = x.CaseNumber + " / " +
                                   x.ItemNumber + " — " + x.Description
                        })
                        .ToListAsync();
                    break;

                case PropertyRegisterKind.FoundProperty:
                    items = await database.FoundPropertyItems
                        .AsNoTracking()
                        .Select(x => new ItemChoice
                        {
                            Id = x.Id,
                            Name = x.ReportNumber + " / " +
                                   x.ItemNumber + " — " + x.Description
                        })
                        .ToListAsync();
                    break;

                case PropertyRegisterKind.Safekeeping:
                    items = await database.SafekeepingItems
                        .AsNoTracking()
                        .Select(x => new ItemChoice
                        {
                            Id = x.Id,
                            Name = x.ReportNumber + " / " +
                                   x.ItemNumber + " — " + x.Description
                        })
                        .ToListAsync();
                    break;

                default:
                    throw new InvalidOperationException(
                        "Unknown register.");
            }

            ItemBox.ItemsSource = items
                .OrderBy(x => x.Name)
                .ToList();

            if (items.Count == 0)
            {
                ShowStatus("This register has no items.", false);
            }
        }
        catch (Exception)
        {
            ShowStatus("Items could not be loaded.", true);
        }
        finally
        {
            _busy = false;
            SelectionPanel.IsEnabled = _ready;
        }
    }

    private async void ItemBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_ready || _busy)
        {
            return;
        }

        await RefreshSelectedItemAsync();
    }

    private async Task<bool> RefreshSelectedItemAsync()
    {
        int version = ++_selectionVersion;
        _loadingItem = true;

        ClearDisplayedItem();
        HistoryBox.Text = "Loading custody history…";
        StatusText.Visibility = Visibility.Collapsed;

        if (RegisterBox.SelectedItem is not RegisterChoice register ||
            ItemBox.SelectedItem is not ItemChoice item)
        {
            _loadingItem = false;
            HistoryBox.Text = "Select an item to view its custody history.";
            return false;
        }

        try
        {
            await using var database =
                DevelopmentDatabase.CreateContext();

            var user = await database.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == _userId);

            if (!IsActiveUser(user))
            {
                if (version == _selectionVersion)
                {
                    _ready = false;
                    _canManage = false;
                    SelectionPanel.IsEnabled = false;
                }

                throw new InvalidOperationException(
                    "Your account cannot access custody records.");
            }

            bool canManage = CanManage(user);

            var state = await LoadStateAsync(
                database,
                register.Type,
                item.Id);

            var locationLabels =
                await _storageDisplayService.GetLabelsAsync(_userId);

            var destinations = new List<LocationChoice>();

            if (canManage)
            {
                var destinationIds = await database.StorageLocations
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .Select(x => x.Id)
                    .ToListAsync();

                destinations = destinationIds
                    .Select(id => new LocationChoice
                    {
                        Id = id,
                        Name = StorageDisplayService.GetLabel(
                            id,
                            locationLabels)
                    })
                    .OrderBy(x => x.Name)
                    .ToList();
            }

            var query = database.CustodyEvents.AsNoTracking();

            query = register.Type switch
            {
                PropertyRegisterKind.Evidence =>
                    query.Where(x => x.EvidenceItemId == item.Id),

                PropertyRegisterKind.FoundProperty =>
                    query.Where(x => x.FoundPropertyItemId == item.Id),

                PropertyRegisterKind.Safekeeping =>
                    query.Where(x => x.SafekeepingItemId == item.Id),

                _ => throw new InvalidOperationException(
                    "Unknown register.")
            };

            var events = await query
                .Select(x => new HistoryEntry
                {
                    Id = x.Id,
                    EventType = x.EventType,
                    OccurredAt = x.OccurredAt,
                    RecordedAt = x.RecordedAt,
                    RecordedByUserId = x.RecordedByUserId,
                    FromCustodian = x.FromCustodian,
                    ToCustodian = x.ToCustodian,
                    FromStorageLocationId = x.FromStorageLocationId,
                    ToStorageLocationId = x.ToStorageLocationId,
                    Reason = canManage ? x.Reason : string.Empty,
                    Notes = canManage ? x.Notes : string.Empty
                })
                .ToListAsync();

            var names = await database.Users
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName);

            if (version != _selectionVersion)
            {
                return false;
            }

            _displayName = user!.DisplayName;
            _canManage = canManage;
            _locationLabels = locationLabels;
            _displayedStatus = state.Status;
            _displayedLocationId = state.LocationId;

            _historyEntries = events;
            _historyNames = names;
            _historyLoaded = true;

            // The latest recipient is independent of display order.
            _lastRecipient = events
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.RecordedAt)
                .ThenByDescending(x => x.Id)
                .Select(x => x.ToCustodian)
                .FirstOrDefault() ?? string.Empty;

            CurrentLocationText.Text =
                $"Status: {ReadableStatus(state.Status)}\n" +
                $"Current storage: {LocationPath(state.LocationId)}";

            CurrentCustodianText.Text =
                string.IsNullOrWhiteSpace(_lastRecipient)
                    ? "Last recorded recipient: Not recorded"
                    : $"Last recorded recipient: {_lastRecipient}";

            RenderHistory();
            NewestFirstBox.IsEnabled = events.Count > 1;

            DestinationBox.ItemsSource = destinations;

            var actions = new List<ActionChoice>();

            if (_canManage)
            {
                if (state.Status == EvidenceStatus.InCustody &&
                    state.LocationId.HasValue)
                {
                    actions.Add(new ActionChoice
                    {
                        Action = CustodyEventType.Transfer,
                        Name = "Storage transfer"
                    });

                    actions.Add(new ActionChoice
                    {
                        Action = CustodyEventType.CheckOut,
                        Name = "Check out"
                    });
                }
                else if (state.Status == EvidenceStatus.CheckedOut &&
                         state.LocationId is null)
                {
                    actions.Add(new ActionChoice
                    {
                        Action = CustodyEventType.CheckIn,
                        Name = "Check in"
                    });
                }
            }

            ActionBox.ItemsSource = actions;
            SetMovementVisibility(_canManage);

            if (actions.Count > 0)
            {
                ActionBox.SelectedIndex = 0;
            }

            _loadingItem = false;
            UpdateActionForm();

            if (!_canManage)
            {
                ShowStatus(
                    "Read-only custody history. Permanent storage " +
                    "locations and internal reasons and notes are restricted.",
                    false);
            }
            else if (actions.Count == 0)
            {
                ShowStatus(
                    "No supported movement is available for this " +
                    "item's current status and storage state.",
                    true);
            }

            return true;
        }
        catch (Exception)
        {
            if (version == _selectionVersion)
            {
                ClearDisplayedItem();
                HistoryBox.Text = "Custody history is unavailable.";
                ShowStatus("Item details could not be loaded.", true);
            }

            return false;
        }
        finally
        {
            if (version == _selectionVersion)
            {
                _loadingItem = false;
            }
        }
    }

    private void HistoryOrder_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (HistoryBox is null ||
            HistoryOrderText is null ||
            !_historyLoaded)
        {
            return;
        }

        RenderHistory();
    }

    private void RenderHistory()
    {
        if (!_historyLoaded)
        {
            return;
        }

        bool newestFirst = NewestFirstBox.IsChecked == true;

        var ordered = newestFirst
            ? _historyEntries
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.RecordedAt)
                .ThenByDescending(x => x.Id)
            : _historyEntries
                .OrderBy(x => x.OccurredAt)
                .ThenBy(x => x.RecordedAt)
                .ThenBy(x => x.Id);

        string orderLabel = newestFirst
            ? "Newest first"
            : "Oldest first";

        HistoryOrderText.Text =
            $"{_historyEntries.Count} custody events • {orderLabel}";

        HistoryBox.Text = _historyEntries.Count == 0
            ? "No custody events recorded."
            : string.Join(
                "\n\n────────────────────────\n\n",
                ordered.Select(x =>
                    FormatHistoryEntry(
                        x,
                        _historyNames,
                        _canManage)));

        HistoryBox.CaretIndex = 0;
        HistoryBox.ScrollToHome();
    }

    private string FormatHistoryEntry(
        HistoryEntry entry,
        IReadOnlyDictionary<Guid, string> names,
        bool includeInternalDetails)
    {
        string text =
            $"{ReadableEvent(entry.EventType)} — " +
            $"{entry.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}\n" +
            $"From: {entry.FromCustodian}\n" +
            $"To: {entry.ToCustodian}\n" +
            $"From storage: {LocationPath(entry.FromStorageLocationId)}\n" +
            $"To storage: {LocationPath(entry.ToStorageLocationId)}\n";

        if (includeInternalDetails)
        {
            text +=
                $"Reason: {entry.Reason}\n" +
                $"Notes: {entry.Notes}\n";
        }

        string recordedBy = names.TryGetValue(
            entry.RecordedByUserId,
            out string? name)
                ? name
                : "Unknown user";

        return text +
            $"Recorded by: {recordedBy}\n" +
            $"Recorded: {entry.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}";
    }

    private void ActionBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_ready && !_loadingItem && !_busy)
        {
            UpdateActionForm();
        }
    }

    private void UpdateActionForm()
    {
        bool allowed = !_busy &&
                       !_loadingItem &&
                       _canManage &&
                       _displayedStatus.HasValue &&
                       ActionBox.SelectedItem is ActionChoice;

        MovementPanel.IsEnabled = allowed;
        SaveButton.IsEnabled = allowed;

        if (ActionBox.SelectedItem is not ActionChoice choice)
        {
            return;
        }

        DestinationBox.SelectedIndex = -1;
        ReasonBox.Clear();
        NotesBox.Clear();

        FromCustodianBox.Text = _displayName;
        ToCustodianBox.Text = _displayName;

        DestinationPanel.Visibility =
            choice.Action == CustodyEventType.CheckOut
                ? Visibility.Collapsed
                : Visibility.Visible;

        switch (choice.Action)
        {
            case CustodyEventType.Transfer:
                FromLabel.Text = "Handed over by *";
                ToLabel.Text = "Received by *";
                SaveButton.Content = "Record Storage Transfer";
                ActionHelpText.Text =
                    "Record the physical storage transfer. " +
                    "The item remains in custody.";
                break;

            case CustodyEventType.CheckOut:
                FromLabel.Text = "Released by *";
                ToLabel.Text = "Checked out to *";
                ToCustodianBox.Clear();
                SaveButton.Content = "Record Checkout";
                ActionHelpText.Text =
                    "Record temporary custody outside storage. " +
                    "Checkout clears the current storage location; " +
                    "it does not record a final release.";
                break;

            case CustodyEventType.CheckIn:
                FromLabel.Text = "Returned by *";
                ToLabel.Text = "Received into storage by *";
                FromCustodianBox.Text = _lastRecipient;
                SaveButton.Content = "Record Check-in";
                ActionHelpText.Text =
                    "Confirm the returning custodian and select " +
                    "the storage location receiving the item.";
                break;
        }
    }

    private async void SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy || _loadingItem)
        {
            return;
        }

        if (!_canManage)
        {
            ShowStatus(
                "Patrol accounts cannot record custody movements.",
                true);
            return;
        }

        if (RegisterBox.SelectedItem is not RegisterChoice register ||
            ItemBox.SelectedItem is not ItemChoice item ||
            ActionBox.SelectedItem is not ActionChoice action ||
            _displayedStatus is not EvidenceStatus expectedStatus)
        {
            ShowStatus("Select a register, item, and action.", true);
            return;
        }

        Guid? destinationId = null;

        if (action.Action != CustodyEventType.CheckOut)
        {
            if (DestinationBox.SelectedItem is not LocationChoice destination)
            {
                ShowStatus(
                    "Choose a destination storage location.",
                    true);
                return;
            }

            destinationId = destination.Id;
        }

        string from = FromCustodianBox.Text.Trim();
        string to = ToCustodianBox.Text.Trim();
        string reason = ReasonBox.Text.Trim();
        string notes = NotesBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(from) ||
            string.IsNullOrWhiteSpace(to) ||
            string.IsNullOrWhiteSpace(reason))
        {
            ShowStatus("Enter both custodians and a reason.", true);
            return;
        }

        string actionName = action.Name;

        _busy = true;
        SelectionPanel.IsEnabled = false;
        MovementPanel.IsEnabled = false;
        SaveButton.IsEnabled = false;
        NewestFirstBox.IsEnabled = false;
        StatusText.Visibility = Visibility.Collapsed;

        bool saved = false;

        try
        {
            await _custodyService.RecordMovementAsync(
                _userId,
                register.Type,
                item.Id,
                action.Action,
                expectedStatus,
                _displayedLocationId,
                destinationId,
                from,
                to,
                reason,
                notes);

            saved = true;
        }
        catch (InvalidOperationException ex)
        {
            ShowStatus(ex.Message, true);
        }
        catch (Exception)
        {
            ShowStatus(
                "Movement could not be saved. " +
                "Your entries have been retained.",
                true);
        }
        finally
        {
            _busy = false;
            SelectionPanel.IsEnabled = _ready;

            bool allowed = _canManage &&
                           _displayedStatus.HasValue &&
                           ActionBox.SelectedItem is ActionChoice;

            MovementPanel.IsEnabled = allowed;
            SaveButton.IsEnabled = allowed;

            NewestFirstBox.IsEnabled =
                _historyLoaded && _historyEntries.Count > 1;
        }

        if (saved)
        {
            bool refreshed = await RefreshSelectedItemAsync();

            ShowStatus(
                refreshed
                    ? $"{actionName} saved. Item state and history updated."
                    : $"{actionName} saved, but the display could not refresh. " +
                      "Reopen Chain of Custody.",
                !refreshed);
        }
    }

    private static async Task<ItemState> LoadStateAsync(
        ManifestDbContext database,
        PropertyRegisterKind register,
        Guid itemId)
    {
        switch (register)
        {
            case PropertyRegisterKind.Evidence:
                {
                    var item = await database.EvidenceItems
                        .AsNoTracking()
                        .SingleAsync(x => x.Id == itemId);

                    return new ItemState(
                        item.Status,
                        item.CurrentStorageLocationId);
                }

            case PropertyRegisterKind.FoundProperty:
                {
                    var item = await database.FoundPropertyItems
                        .AsNoTracking()
                        .SingleAsync(x => x.Id == itemId);

                    return new ItemState(
                        item.Status,
                        item.CurrentStorageLocationId);
                }

            case PropertyRegisterKind.Safekeeping:
                {
                    var item = await database.SafekeepingItems
                        .AsNoTracking()
                        .SingleAsync(x => x.Id == itemId);

                    return new ItemState(
                        item.Status,
                        item.CurrentStorageLocationId);
                }

            default:
                throw new InvalidOperationException("Unknown register.");
        }
    }

    private string LocationPath(Guid? locationId)
    {
        return StorageDisplayService.GetLabel(
            locationId,
            _locationLabels);
    }

    private static string ReadableEvent(CustodyEventType type)
    {
        return type switch
        {
            CustodyEventType.Intake => "Intake",
            CustodyEventType.Transfer => "Storage Transfer",
            CustodyEventType.CheckOut => "Check Out",
            CustodyEventType.CheckIn => "Check In",
            CustodyEventType.Release => "Release",
            CustodyEventType.Disposal => "Disposal",
            _ => type.ToString()
        };
    }

    private static string ReadableStatus(EvidenceStatus status)
    {
        return status switch
        {
            EvidenceStatus.InCustody => "In custody",
            EvidenceStatus.CheckedOut => "Checked out",
            EvidenceStatus.Released => "Released",
            EvidenceStatus.Disposed => "Disposed",
            _ => status.ToString()
        };
    }

    private void ShowStatus(string message, bool error)
    {
        StatusText.Text = message;

        StatusText.Foreground = error
            ? new SolidColorBrush(Color.FromRgb(255, 155, 155))
            : new SolidColorBrush(Color.FromRgb(134, 239, 172));

        StatusText.Visibility = Visibility.Visible;
    }

    public sealed class RegisterChoice
    {
        public PropertyRegisterKind Type { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class ItemChoice
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class LocationChoice
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class ActionChoice
    {
        public CustodyEventType Action { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class HistoryEntry
    {
        public Guid Id { get; set; }
        public CustodyEventType EventType { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public DateTimeOffset RecordedAt { get; set; }
        public Guid RecordedByUserId { get; set; }
        public string FromCustodian { get; set; } = string.Empty;
        public string ToCustodian { get; set; } = string.Empty;
        public Guid? FromStorageLocationId { get; set; }
        public Guid? ToStorageLocationId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    private sealed record ItemState(
        EvidenceStatus Status,
        Guid? LocationId);
}