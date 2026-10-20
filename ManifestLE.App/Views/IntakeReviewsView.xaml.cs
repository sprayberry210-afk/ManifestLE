using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.App.Views;

public partial class IntakeReviewsView : UserControl
{
    private const string SelectionPrompt =
        "Select a submission to view its details.";

    private readonly Guid _userId;
    private readonly IntakeReviewQueueService _service = new();
    private readonly IntakeReviewDecisionService _decisions = new();

    private List<IntakeReviewQueueRow> _rows = new();

    private bool _initialized;
    private bool _loading;
    private bool _hasAccess;
    private bool _canReview;

    private IntakeReviewQueueRow? _selectedReview;

    public IntakeReviewsView()
        : this(Guid.Empty)
    {
    }

    public IntakeReviewsView(Guid userId)
    {
        InitializeComponent();

        _userId = userId;

        OpenFilter.IsChecked = true;
        _initialized = true;

        Loaded += IntakeReviewsView_Loaded;
    }

    private async void IntakeReviewsView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= IntakeReviewsView_Loaded;
        await LoadQueueAsync();
    }

    private async void RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadQueueAsync();
    }

    private void ClearQueue()
    {
        _hasAccess = false;
        _canReview = false;
        _rows.Clear();

        ClearSelection();

        ReviewsGrid.ItemsSource = null;
        PermanentLocationBox.ItemsSource = null;
    }

    private void ClearSelection()
    {
        _selectedReview = null;

        if (DecisionCard is null)
        {
            return;
        }

        DecisionCard.Visibility = Visibility.Collapsed;
        CorrectionCard.Visibility = Visibility.Collapsed;

        DecisionItemText.Text = string.Empty;
        CorrectionInstructionsBox.Clear();
        ReceiptNotesBox.Clear();
        ReceiptConfirmedBox.IsChecked = false;
        PermanentLocationBox.SelectedIndex = -1;
        DetailsBox.Text = SelectionPrompt;
    }

    private async Task LoadDataAsync()
    {
        ClearQueue();
        UpdateControls();

        bool canReview = await _service.CanReviewAsync(_userId);
        var rows = await _service.GetQueueAsync(_userId);

        var destinations = canReview
            ? await _service.GetPermanentDestinationsAsync(_userId)
            : new List<IntakeReviewDestination>();

        _rows = rows;
        _canReview = canReview;
        _hasAccess = true;

        PermanentLocationBox.ItemsSource = destinations;
        ApplyFilters();
    }

    private async Task LoadQueueAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        UpdateControls();

        SummaryText.Text = "Loading submissions…";
        StatusText.Visibility = Visibility.Collapsed;

        try
        {
            await LoadDataAsync();
        }
        catch (InvalidOperationException ex)
        {
            ClearQueue();
            SummaryText.Text = "Submissions are unavailable.";
            ShowStatus(ex.Message, true);
        }
        catch (Exception)
        {
            ClearQueue();
            SummaryText.Text = "Submissions are unavailable.";

            ShowStatus(
                "The intake queue could not be loaded. " +
                "Select Refresh to try again.",
                true);
        }
        finally
        {
            _loading = false;
            UpdateControls();
        }
    }

    private void SearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_initialized && !_loading && _hasAccess)
        {
            ApplyFilters();
        }
    }

    private void Filter_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_initialized && !_loading && _hasAccess)
        {
            ApplyFilters();
        }
    }

    private void ApplyFilters()
    {
        ClearSelection();

        string search = SearchBox.Text.Trim();

        var filtered = _rows.Where(row =>
        {
            bool matchesStatus =
                AllFilter.IsChecked == true ||
                (CorrectionFilter.IsChecked == true
                    ? row.Status == IntakeReviewStatus.NeedsCorrection
                    : row.Status != IntakeReviewStatus.Accepted);

            bool matchesSearch =
                search.Length == 0 ||
                Contains(row.ReportNumber, search) ||
                Contains(row.ItemNumber, search) ||
                Contains(row.Description, search) ||
                Contains(row.SubmittedBy, search) ||
                Contains(row.RegisterText, search) ||
                Contains(row.StatusText, search);

            return matchesStatus && matchesSearch;
        }).ToList();

        ReviewsGrid.ItemsSource = filtered;

        int openCount = _rows.Count(x =>
            x.Status != IntakeReviewStatus.Accepted);

        int correctionCount = _rows.Count(x =>
            x.Status == IntakeReviewStatus.NeedsCorrection);

        SummaryText.Text =
            $"Showing {filtered.Count} of {_rows.Count} submissions. " +
            $"Open: {openCount}. Needs correction: {correctionCount}.";

        if (filtered.Count == 0)
        {
            ShowStatus(
                _rows.Count == 0
                    ? "No intake submissions are available for your account."
                    : "No submissions match the current filters.",
                false);
        }
        else
        {
            StatusText.Visibility = Visibility.Collapsed;
        }

        UpdateControls();
    }

    private static bool Contains(string value, string search)
    {
        return value.Contains(
            search,
            StringComparison.OrdinalIgnoreCase);
    }

    private void ReviewsGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (DetailsBox is null || _loading)
        {
            return;
        }

        ClearSelection();

        if (ReviewsGrid.SelectedItem is not IntakeReviewQueueRow row)
        {
            UpdateControls();
            return;
        }

        _selectedReview = row;

        string instructions =
            string.IsNullOrWhiteSpace(row.CorrectionInstructions)
                ? "No correction instructions recorded."
                : row.CorrectionInstructions;

        DetailsBox.Text =
            $"Register: {row.RegisterText}\n" +
            $"Case / report: {row.ReportNumber}\n" +
            $"Item: {row.ItemNumber}\n" +
            $"Description: {row.Description}\n\n" +
            $"Review status: {row.StatusText}\n" +
            $"Current storage: {row.Storage}\n\n" +
            $"Submitted by: {row.SubmittedBy}\n" +
            $"First submitted: " +
            $"{row.SubmittedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}\n" +
            $"Last submitted: " +
            $"{row.LastSubmittedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}\n\n" +
            $"Correction instructions:\n{instructions}";

        DecisionItemText.Text =
            $"{row.RegisterText} | {row.ReportNumber} | " +
            $"Item {row.ItemNumber} — {row.Description}";

        UpdateControls();
    }

    private bool CanDecide()
    {
        return _hasAccess &&
               _canReview &&
               _selectedReview is not null &&
               (_selectedReview.Status == IntakeReviewStatus.PendingReview ||
                _selectedReview.Status == IntakeReviewStatus.Resubmitted);
    }

    private bool CanCorrect()
    {
        return _hasAccess &&
               _selectedReview is not null &&
               _selectedReview.SubmittedByUserId == _userId &&
               _selectedReview.Status == IntakeReviewStatus.NeedsCorrection;
    }

    private async void CorrectAndResubmitButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_loading || !CanCorrect())
        {
            return;
        }

        var row = _selectedReview!;
        bool resubmitted = false;

        _loading = true;
        UpdateControls();

        try
        {
            var correctionWindow = new IntakeCorrectionWindow(
                _userId,
                row.ReviewId);

            var owner = Window.GetWindow(this);

            if (owner is not null)
            {
                correctionWindow.Owner = owner;
            }

            resubmitted = correctionWindow.ShowDialog() == true;
        }
        catch (Exception)
        {
            ShowStatus(
                "The correction window could not be opened. " +
                "Refresh the queue and try again.",
                true);
        }
        finally
        {
            _loading = false;
            UpdateControls();
        }

        if (resubmitted)
        {
            await LoadQueueAsync();

            if (_hasAccess)
            {
                ShowStatus(
                    "Correction saved and resubmitted for review. " +
                    "Physical custody has not changed.",
                    false);
            }
        }
    }

    private async void RequestCorrectionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_loading || !CanDecide())
        {
            return;
        }

        var row = _selectedReview!;
        string instructions = CorrectionInstructionsBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(instructions))
        {
            ShowStatus("Enter the correction instructions.", true);
            CorrectionInstructionsBox.Focus();
            return;
        }

        var answer = MessageBox.Show(
            $"Request correction for {row.ReportNumber}, " +
            $"item {row.ItemNumber}?\n\n" +
            "The property will remain at its current storage location.",
            "Request Intake Correction",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await RunDecisionAsync(
            () => _decisions.RequestCorrectionAsync(
                _userId,
                row.ReviewId,
                row.Revision,
                instructions),
            "Correction requested. The submitting officer can " +
            "view the instructions in Intake Reviews. " +
            "Physical custody has not changed.");
    }

    private async void AcceptButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_loading || !CanDecide())
        {
            return;
        }

        var row = _selectedReview!;

        if (row.CurrentStorageLocationId is not Guid sourceId)
        {
            ShowStatus(
                "The current storage location is unavailable. " +
                "Refresh the queue.",
                true);
            return;
        }

        if (PermanentLocationBox.SelectedItem
            is not IntakeReviewDestination destination)
        {
            ShowStatus("Choose a permanent storage location.", true);
            return;
        }

        if (ReceiptConfirmedBox.IsChecked != true)
        {
            ShowStatus(
                "Confirm physical receipt and packaging verification.",
                true);
            return;
        }

        string notes =
            "Reviewer confirmed physical receipt and packaging verification.";

        string enteredNotes = ReceiptNotesBox.Text.Trim();

        if (enteredNotes.Length > 0)
        {
            notes += "\n" + enteredNotes;
        }

        var answer = MessageBox.Show(
            $"Accept {row.ReportNumber}, item {row.ItemNumber}, " +
            $"into:\n\n{destination.Name}\n\n" +
            "Confirm this reflects the item's physical movement.",
            "Accept into Permanent Storage",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await RunDecisionAsync(
            () => _decisions.AcceptAsync(
                _userId,
                row.ReviewId,
                row.Revision,
                sourceId,
                destination.Id,
                notes),
            "Item accepted into permanent storage. " +
            "The custody transfer and review decision were recorded. " +
            "Select All to view accepted submissions.");
    }

    private async Task RunDecisionAsync(
        Func<Task> decision,
        string successMessage)
    {
        _loading = true;
        UpdateControls();
        StatusText.Visibility = Visibility.Collapsed;

        try
        {
            await decision();

            try
            {
                await LoadDataAsync();
                ShowStatus(successMessage, false);
            }
            catch (Exception)
            {
                ClearQueue();

                ShowStatus(
                    "The decision was saved, but the queue could not " +
                    "refresh. Reopen Intake Reviews.",
                    true);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            ShowStatus(
                "This review changed. Refresh the queue " +
                "before making another decision.",
                true);
        }
        catch (InvalidOperationException ex)
        {
            ShowStatus(ex.Message, true);
        }
        catch (Exception)
        {
            ShowStatus(
                "The decision could not be saved. Refresh the queue " +
                "and check its status before trying again.",
                true);
        }
        finally
        {
            _loading = false;
            UpdateControls();
        }
    }

    private void UpdateControls()
    {
        RefreshButton.IsEnabled = !_loading;
        SearchBox.IsEnabled = !_loading;
        FilterPanel.IsEnabled = !_loading;
        ReviewsGrid.IsEnabled = _hasAccess && !_loading;

        bool canDecide = CanDecide();
        bool canCorrect = CanCorrect();

        DecisionCard.Visibility = canDecide
            ? Visibility.Visible
            : Visibility.Collapsed;

        DecisionPanel.IsEnabled = canDecide && !_loading;

        CorrectionCard.Visibility = canCorrect
            ? Visibility.Visible
            : Visibility.Collapsed;

        CorrectAndResubmitButton.IsEnabled =
            canCorrect && !_loading;
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;

        StatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(255, 155, 155))
            : new SolidColorBrush(Color.FromRgb(134, 239, 172));

        StatusText.Visibility = Visibility.Visible;
    }
}