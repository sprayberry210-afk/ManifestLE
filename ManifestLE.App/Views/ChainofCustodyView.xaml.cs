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

    private bool _ready;
    private bool _busy;
    private bool _loadingItem;
    private bool _canManage;
    private int _selectionVersion;

    private EvidenceStatus? _displayedStatus;
    private Guid? _displayedLocationId;

    private string _displayName = string.Empty;
    private string _lastRecipient = string.Empty;

    private List<StorageLocation> _locations = new();

    public ChainOfCustodyView()
        : this(Guid.Empty)
    {
    }

    public ChainOfCustodyView(Guid userId)
    {
        InitializeComponent();

        _userId = userId;

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
                SelectionPanel.IsEnabled = false;
                ShowStatus("Your account cannot access custody records.", true);
                return;
            }

            _displayName = user!.DisplayName;
            _canManage = CanManage(user);

            _ready = true;
            RegisterBox.SelectedIndex = 0;
        }
        catch (Exception)
        {
            ShowStatus("Custody records could not be initialized.", true);
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
        MovementPanel.IsEnabled = false;
        SaveButton.IsEnabled = false;
        _displayedStatus = null;

        ItemBox.ItemsSource = null;
        ActionBox.ItemsSource = null;
        CurrentLocationText.Text = string.Empty;
        CurrentCustodianText.Text = string.Empty;
        HistoryBox.Text = "Select an item to view its custody history.";
        StatusText.Visibility = Visibility.Collapsed;

        try
        {
            await using var database =
                DevelopmentDatabase.CreateContext();

            List<ItemChoice> items;

            switch (register.Type)
            {
                case PropertyRegisterKind.Evidence:
                    items = await database.EvidenceItems.AsNoTracking()
                        .Select(x => new ItemChoice
                        {
                            Id = x.Id,
                            Name = x.CaseNumber + " / " +
                                   x.ItemNumber + " — " + x.Description
                        })
                        .ToListAsync();
                    break;

                case PropertyRegisterKind.FoundProperty:
                    items = await database.FoundPropertyItems.AsNoTracking()
                        .Select(x => new ItemChoice
                        {
                            Id = x.Id,
                            Name = x.ReportNumber + " / " +
                                   x.ItemNumber + " — " + x.Description
                        })
                        .ToListAsync();
                    break;

                case PropertyRegisterKind.Safekeeping:
                    items = await database.SafekeepingItems.AsNoTracking()
                        .Select(x => new ItemChoice
                        {
                            Id = x.Id,
                            Name = x.ReportNumber + " / " +
                                   x.ItemNumber + " — " + x.Description
                        })
                        .ToListAsync();
                    break;

                default:
                    throw new InvalidOperationException("Unknown register.");
            }

            ItemBox.ItemsSource = items.OrderBy(x => x.Name).ToList();

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
            SelectionPanel.IsEnabled = true;
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

        MovementPanel.IsEnabled = false;
        SaveButton.IsEnabled = false;
        _displayedStatus = null;
        ActionBox.ItemsSource = null;

        FromCustodianBox.Clear();
        ToCustodianBox.Clear();
        ReasonBox.Clear();
        NotesBox.Clear();
        CurrentLocationText.Text = string.Empty;
        CurrentCustodianText.Text = string.Empty;
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

            var user = await database.Users.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == _userId);

            if (!IsActiveUser(user))
            {
                throw new InvalidOperationException(
                    "Your account cannot access custody records.");
            }

            var state = await LoadStateAsync(
                database,
                register.Type,
                item.Id);

            var locations = await database.StorageLocations
                .AsNoTracking()
                .ToListAsync();

            var query = database.CustodyEvents.AsNoTracking();

            query = register.Type switch
            {
                PropertyRegisterKind.Evidence =>
                    query.Where(x => x.EvidenceItemId == item.Id),

                PropertyRegisterKind.FoundProperty =>
                    query.Where(x => x.FoundPropertyItemId == item.Id),

                _ => query.Where(x => x.SafekeepingItemId == item.Id)
            };

            var events = await query.ToListAsync();

            var names = await database.Users.AsNoTracking()
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName);

            if (version != _selectionVersion)
            {
                return false;
            }

            _displayName = user!.DisplayName;
            _canManage = CanManage(user);
            _locations = locations;
            _displayedStatus = state.Status;
            _displayedLocationId = state.LocationId;

            var orderedEvents = events
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.RecordedAt)
                .ToList();

            _lastRecipient = orderedEvents
                .Select(x => x.ToCustodian)
                .FirstOrDefault() ?? string.Empty;

            CurrentLocationText.Text =
                $"Status: {ReadableStatus(state.Status)}\n" +
                $"Current storage: {LocationPath(state.LocationId)}";

            CurrentCustodianText.Text =
                string.IsNullOrWhiteSpace(_lastRecipient)
                    ? "Last recorded recipient: Not recorded"
                    : $"Last recorded recipient: {_lastRecipient}";

            DestinationBox.ItemsSource = _locations
                .Where(x => x.IsActive)
                .Select(x => new LocationChoice
                {
                    Id = x.Id,
                    Name = LocationPath(x.Id)
                })
                .OrderBy(x => x.Name)
                .ToList();

            HistoryBox.Text = orderedEvents.Count == 0
                ? "No custody events recorded."
                : string.Join(
                    "\n\n────────────────────────\n\n",
                    orderedEvents.Select(x =>
                        $"{x.EventType} — " +
                        $"{x.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}\n" +
                        $"From: {x.FromCustodian}\n" +
                        $"To: {x.ToCustodian}\n" +
                        $"From storage: {LocationPath(x.FromStorageLocationId)}\n" +
                        $"To storage: {LocationPath(x.ToStorageLocationId)}\n" +
                        $"Reason: {x.Reason}\n" +
                        $"Notes: {x.Notes}\n" +
                        $"Recorded by: {names.GetValueOrDefault(x.RecordedByUserId, "Unknown user")}\n" +
                        $"Recorded: {x.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}"));

            var actions = new List<ActionChoice>();

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

            ActionBox.ItemsSource = actions;

            if (actions.Count > 0)
            {
                ActionBox.SelectedIndex = 0;
            }

            _loadingItem = false;
            UpdateActionForm();

            if (!_canManage)
            {
                ShowStatus(
                    "An administrator or evidence custodian " +
                    "must record custody movements.",
                    true);
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

        bool checkout = choice.Action == CustodyEventType.CheckOut;

        DestinationPanel.Visibility = checkout
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
                ShowStatus("Choose a destination storage location.", true);
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
            SelectionPanel.IsEnabled = true;

            // Keep entered values on failure.
            MovementPanel.IsEnabled = _canManage;
            SaveButton.IsEnabled = _canManage &&
                                   _displayedStatus.HasValue;
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
        if (locationId is null)
        {
            return "None";
        }

        var parts = new List<string>();
        var visited = new HashSet<Guid>();

        while (locationId is Guid id)
        {
            if (!visited.Add(id))
            {
                parts.Add("[Location cycle]");
                break;
            }

            var location = _locations.FirstOrDefault(x => x.Id == id);

            if (location is null)
            {
                parts.Add("[Location unavailable]");
                break;
            }

            parts.Add(location.IsActive
                ? location.Name
                : $"{location.Name} (inactive)");

            locationId = location.ParentLocationId;
        }

        parts.Reverse();
        return string.Join(" / ", parts);
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

    private sealed record ItemState(
        EvidenceStatus Status,
        Guid? LocationId);
}