using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.App.Views;

public enum PropertyRegisterType
{
    Evidence,
    FoundProperty,
    Safekeeping
}

public partial class PropertyRegisterView : UserControl
{
    private const string SelectionPrompt =
        "Select an item to view its details.";

    private readonly Guid _userId;
    private readonly PropertyRegisterType _register;
    private readonly StorageDisplayService _storageDisplayService = new();

    private List<RegisterRow> _rows = new();

    private bool _isLoading;
    private bool _initialized;
    private bool _hasAccess;

    public PropertyRegisterView()
        : this(Guid.Empty, PropertyRegisterType.Evidence)
    {
    }

    public PropertyRegisterView(
        Guid userId,
        PropertyRegisterType register)
    {
        InitializeComponent();

        _userId = userId;
        _register = register;

        HeadingText.Text = register switch
        {
            PropertyRegisterType.Evidence => "Evidence Records",
            PropertyRegisterType.FoundProperty => "Found Property",
            PropertyRegisterType.Safekeeping => "Safekeeping",
            _ => "Property Records"
        };

        RecordsGrid.Sorting += RecordsGrid_Sorting;

        foreach (var column in RecordsGrid.Columns)
        {
            if (column is DataGridTextColumn textColumn &&
                textColumn.Binding is Binding binding &&
                binding.Path?.Path == nameof(RegisterRow.CollectedLocal))
            {
                column.SortMemberPath = nameof(RegisterRow.CollectedAt);
            }
        }

        AllStatusButton.IsChecked = true;
        _initialized = true;

        Loaded += PropertyRegisterView_Loaded;
    }

    private async void PropertyRegisterView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= PropertyRegisterView_Loaded;
        await LoadRecordsAsync();
    }

    private async void RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadRecordsAsync();
    }

    private async Task LoadRecordsAsync()
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        _hasAccess = false;
        RefreshButton.IsEnabled = false;

        _rows.Clear();
        RecordsGrid.ItemsSource = null;
        DetailsBox.Text = SelectionPrompt;
        StatusText.Text = "Loading records…";

        try
        {
            await using var database =
                DevelopmentDatabase.CreateContext();

            var user = await database.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == _userId);

            if (user is null ||
                !user.IsActive ||
                (user.Role != UserRole.Officer &&
                 user.Role != UserRole.EvidenceCustodian &&
                 user.Role != UserRole.Administrator))
            {
                DetailsBox.Text = "Access denied.";
                StatusText.Text = "Your account cannot view this register.";
                return;
            }

            var storageLabels =
                await _storageDisplayService.GetLabelsAsync(_userId);

            var rows = new List<RegisterRow>();

            if (_register == PropertyRegisterType.Evidence)
            {
                var items = await database.EvidenceItems
                    .AsNoTracking()
                    .ToListAsync();

                foreach (var item in items)
                {
                    rows.Add(new RegisterRow
                    {
                        Id = item.Id,
                        ReportNumber = item.CaseNumber,
                        ItemNumber = item.ItemNumber,
                        Description = item.Description,
                        Status = item.Status,
                        CollectedAt = item.CollectedAt,
                        CreatedAt = item.CreatedAt,
                        StoragePath = StorageLabel(
                            item.CurrentStorageLocationId,
                            storageLabels),
                        OwnerName = item.OwnerName,
                        Details = BuildDetails(
                            ("Register", "Evidence"),
                            ("Case / report", item.CaseNumber),
                            ("Item number", item.ItemNumber),
                            ("Description", item.Description),
                            ("Collected by", item.CollectedBy),
                            ("Collection time", FormatTime(item.CollectedAt)),
                            ("Collection location", item.CollectionLocation),
                            ("Packaging", item.Packaging),
                            ("Owner", item.OwnerName),
                            ("Owner contact", item.OwnerContact),
                            ("Notes", item.Notes),
                            ("Record ID", item.Id.ToString()))
                    });
                }
            }
            else if (_register == PropertyRegisterType.FoundProperty)
            {
                var items = await database.FoundPropertyItems
                    .AsNoTracking()
                    .ToListAsync();

                foreach (var item in items)
                {
                    rows.Add(new RegisterRow
                    {
                        Id = item.Id,
                        ReportNumber = item.ReportNumber,
                        ItemNumber = item.ItemNumber,
                        Description = item.Description,
                        Status = item.Status,
                        CollectedAt = item.CollectedAt,
                        CreatedAt = item.CreatedAt,
                        StoragePath = StorageLabel(
                            item.CurrentStorageLocationId,
                            storageLabels),
                        OwnerName = item.OwnerName,
                        Details = BuildDetails(
                            ("Register", "Found Property"),
                            ("Case / report", item.ReportNumber),
                            ("Item number", item.ItemNumber),
                            ("Description", item.Description),
                            ("Collected by", item.CollectedBy),
                            ("Collection time", FormatTime(item.CollectedAt)),
                            ("Found location", item.FoundLocation),
                            ("Packaging", item.Packaging),
                            ("Finder", item.FinderName),
                            ("Finder contact", item.FinderContact),
                            ("Owner", item.OwnerName),
                            ("Owner contact", item.OwnerContact),
                            ("Notes", item.Notes),
                            ("Record ID", item.Id.ToString()))
                    });
                }
            }
            else if (_register == PropertyRegisterType.Safekeeping)
            {
                var items = await database.SafekeepingItems
                    .AsNoTracking()
                    .ToListAsync();

                foreach (var item in items)
                {
                    rows.Add(new RegisterRow
                    {
                        Id = item.Id,
                        ReportNumber = item.ReportNumber,
                        ItemNumber = item.ItemNumber,
                        Description = item.Description,
                        Status = item.Status,
                        CollectedAt = item.CollectedAt,
                        CreatedAt = item.CreatedAt,
                        StoragePath = StorageLabel(
                            item.CurrentStorageLocationId,
                            storageLabels),
                        OwnerName = item.OwnerName,
                        SerialNumber = item.SerialNumber,
                        Details = BuildDetails(
                            ("Register", "Safekeeping"),
                            ("Case / report", item.ReportNumber),
                            ("Item number", item.ItemNumber),
                            ("Description", item.Description),
                            ("Collected by", item.CollectedBy),
                            ("Collection time", FormatTime(item.CollectedAt)),
                            ("Collection location", item.CollectionLocation),
                            ("Packaging", item.Packaging),
                            ("Owner", item.OwnerName),
                            ("Owner contact", item.OwnerContact),
                            ("Hold reason", item.HoldReason),
                            ("Hold authority", item.HoldAuthority),
                            ("Review due", item.ReviewDueAt is DateTimeOffset due
                                ? FormatTime(due) : "Not set"),
                            ("Firearm", item.IsFirearm ? "Yes" : "No"),
                            ("Manufacturer", item.Manufacturer),
                            ("Model", item.Model),
                            ("Serial number", item.SerialNumber),
                            ("Caliber / gauge", item.Caliber),
                            ("Notes", item.Notes),
                            ("Record ID", item.Id.ToString()))
                    });
                }
            }
            else
            {
                throw new InvalidOperationException("Unknown register.");
            }

            _rows = rows
                .GroupBy(
                    row => row.ReportNumber.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group =>
                    group.Min(row => row.CreatedAt))
                .ThenBy(
                    group => group.Key,
                    NaturalTextComparer.Instance)
                .SelectMany(group =>
                    group.OrderBy(
                            row => row.ItemNumber,
                            NaturalTextComparer.Instance)
                        .ThenBy(row => row.CreatedAt)
                        .ThenBy(row => row.Id))
                .ToList();

            _hasAccess = true;
            ApplyFilters();
        }
        catch (Exception)
        {
            _hasAccess = false;
            _rows.Clear();
            RecordsGrid.ItemsSource = null;
            DetailsBox.Text = SelectionPrompt;

            StatusText.Text =
                "Records could not be loaded. Select Refresh to try again.";
        }
        finally
        {
            _isLoading = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private static string StorageLabel(
        Guid? locationId,
        IReadOnlyDictionary<Guid, string> labels)
    {
        return locationId is null
            ? "No storage location"
            : StorageDisplayService.GetLabel(locationId, labels);
    }

    private void SearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_initialized && !_isLoading && _hasAccess)
        {
            ApplyFilters();
        }
    }

    private void StatusFilter_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_initialized && !_isLoading && _hasAccess)
        {
            ApplyFilters();
        }
    }

    private void ApplyFilters()
    {
        string search = SearchBox.Text.Trim();

        EvidenceStatus? status =
            InCustodyButton.IsChecked == true
                ? EvidenceStatus.InCustody
                : CheckedOutButton.IsChecked == true
                    ? EvidenceStatus.CheckedOut
                    : ReleasedButton.IsChecked == true
                        ? EvidenceStatus.Released
                        : DisposedButton.IsChecked == true
                            ? EvidenceStatus.Disposed
                            : null;

        var filtered = _rows
            .Where(row =>
                (!status.HasValue || row.Status == status.Value) &&
                (search.Length == 0 ||
                 Contains(row.ReportNumber, search) ||
                 Contains(row.ItemNumber, search) ||
                 Contains(row.Description, search) ||
                 Contains(row.OwnerName, search) ||
                 Contains(row.SerialNumber, search) ||
                 Contains(row.StoragePath, search)))
            .ToList();

        RecordsGrid.ItemsSource = filtered;

        foreach (var column in RecordsGrid.Columns)
        {
            column.SortDirection = null;
        }

        DetailsBox.Text = SelectionPrompt;

        StatusText.Text =
            $"Showing {filtered.Count} of {_rows.Count} records. " +
            "Default order: newest case groups, then item number.";
    }

    private void RecordsGrid_Sorting(
        object sender,
        DataGridSortingEventArgs e)
    {
        string member = e.Column.SortMemberPath;

        bool isItem = member == nameof(RegisterRow.ItemNumber);
        bool isCase = member == nameof(RegisterRow.ReportNumber);

        if (!isItem && !isCase)
        {
            return;
        }

        e.Handled = true;

        var direction =
            e.Column.SortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

        var view = CollectionViewSource.GetDefaultView(
            RecordsGrid.ItemsSource) as ListCollectionView;

        if (view is null)
        {
            return;
        }

        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.CustomSort = new RegisterRowComparer(
                isItem,
                direction);
        }

        foreach (var column in RecordsGrid.Columns)
        {
            column.SortDirection = null;
        }

        e.Column.SortDirection = direction;

        string label = isItem ? "item number" : "case / report";
        string order = direction == ListSortDirection.Ascending
            ? "ascending"
            : "descending";

        StatusText.Text =
            $"Showing {view.Count} of {_rows.Count} records. " +
            $"Sorted by {label}, {order}.";
    }

    private static bool Contains(string value, string search)
    {
        return value.Contains(
            search,
            StringComparison.OrdinalIgnoreCase);
    }

    private void RecordsGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (DetailsBox is null)
        {
            return;
        }

        if (RecordsGrid.SelectedItem is RegisterRow row)
        {
            DetailsBox.Text =
                $"Status: {row.StatusText}\n" +
                $"Storage: {row.StoragePath}\n\n" +
                row.Details;
        }
        else
        {
            DetailsBox.Text = SelectionPrompt;
        }
    }

    private static string BuildDetails(
        params (string Label, string Value)[] fields)
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            fields.Select(field =>
                $"{field.Label}: " +
                (string.IsNullOrWhiteSpace(field.Value)
                    ? "Not recorded"
                    : field.Value)));
    }

    private static string FormatTime(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString(
            "yyyy-MM-dd HH:mm zzz",
            CultureInfo.InvariantCulture);
    }

    private sealed class RegisterRowComparer : IComparer
    {
        private readonly bool _sortByItem;
        private readonly int _direction;

        public RegisterRowComparer(
            bool sortByItem,
            ListSortDirection direction)
        {
            _sortByItem = sortByItem;

            _direction = direction == ListSortDirection.Ascending
                ? 1
                : -1;
        }

        public int Compare(object? x, object? y)
        {
            if (x is not RegisterRow left ||
                y is not RegisterRow right)
            {
                return 0;
            }

            int result = NaturalTextComparer.Instance.Compare(
                _sortByItem ? left.ItemNumber : left.ReportNumber,
                _sortByItem ? right.ItemNumber : right.ReportNumber);

            if (result != 0)
            {
                return _direction * Math.Sign(result);
            }

            result = NaturalTextComparer.Instance.Compare(
                _sortByItem ? left.ReportNumber : left.ItemNumber,
                _sortByItem ? right.ReportNumber : right.ItemNumber);

            return result != 0
                ? result
                : left.Id.CompareTo(right.Id);
        }
    }

    private sealed class NaturalTextComparer : IComparer<string>
    {
        public static NaturalTextComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            string left = (x ?? "").Trim();
            string right = (y ?? "").Trim();

            int leftIndex = 0;
            int rightIndex = 0;

            while (leftIndex < left.Length &&
                   rightIndex < right.Length)
            {
                if (IsDigit(left[leftIndex]) &&
                    IsDigit(right[rightIndex]))
                {
                    int leftStart = leftIndex;
                    int rightStart = rightIndex;

                    while (leftIndex < left.Length &&
                           IsDigit(left[leftIndex]))
                    {
                        leftIndex++;
                    }

                    while (rightIndex < right.Length &&
                           IsDigit(right[rightIndex]))
                    {
                        rightIndex++;
                    }

                    int leftSignificant = leftStart;
                    int rightSignificant = rightStart;

                    while (leftSignificant < leftIndex &&
                           left[leftSignificant] == '0')
                    {
                        leftSignificant++;
                    }

                    while (rightSignificant < rightIndex &&
                           right[rightSignificant] == '0')
                    {
                        rightSignificant++;
                    }

                    int leftDigits = leftIndex - leftSignificant;
                    int rightDigits = rightIndex - rightSignificant;

                    int result = leftDigits.CompareTo(rightDigits);

                    if (result != 0)
                    {
                        return result;
                    }

                    result = string.Compare(
                        left,
                        leftSignificant,
                        right,
                        rightSignificant,
                        leftDigits,
                        StringComparison.Ordinal);

                    if (result != 0)
                    {
                        return result;
                    }
                }
                else
                {
                    int result = char.ToUpperInvariant(left[leftIndex])
                        .CompareTo(
                            char.ToUpperInvariant(right[rightIndex]));

                    if (result != 0)
                    {
                        return result;
                    }

                    leftIndex++;
                    rightIndex++;
                }
            }

            int remaining = (left.Length - leftIndex)
                .CompareTo(right.Length - rightIndex);

            if (remaining != 0)
            {
                return remaining;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }

        private static bool IsDigit(char value)
        {
            return value >= '0' && value <= '9';
        }
    }

    public sealed class RegisterRow
    {
        public Guid Id { get; set; }

        public string ReportNumber { get; set; } = "";

        public string ItemNumber { get; set; } = "";

        public string Description { get; set; } = "";

        public EvidenceStatus Status { get; set; }

        public DateTimeOffset CollectedAt { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public string StoragePath { get; set; } = "";

        public string OwnerName { get; set; } = "";

        public string SerialNumber { get; set; } = "";

        public string Details { get; set; } = "";

        public string CollectedLocal =>
            CollectedAt.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture);

        public string StatusText => Status switch
        {
            EvidenceStatus.InCustody => "In custody",
            EvidenceStatus.CheckedOut => "Checked out",
            EvidenceStatus.Released => "Released",
            EvidenceStatus.Disposed => "Disposed",
            _ => Status.ToString()
        };
    }
}