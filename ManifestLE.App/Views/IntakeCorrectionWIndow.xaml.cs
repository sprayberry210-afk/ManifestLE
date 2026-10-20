using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using ManifestLE.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.App.Views;

public partial class IntakeCorrectionWindow : Window
{
    private readonly Guid _userId;
    private readonly Guid _reviewId;
    private readonly IntakeCorrectionService _service = new();

    private IntakeCorrectionDetails? _details;
    private bool _saving;

    public IntakeCorrectionWindow(Guid userId, Guid reviewId)
    {
        InitializeComponent();

        _userId = userId;
        _reviewId = reviewId;

        PackagingBox.ItemsSource =
            IntakeCorrectionService.PackagingOptions;

        Loaded += IntakeCorrectionWindow_Loaded;
    }

    private async void IntakeCorrectionWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= IntakeCorrectionWindow_Loaded;

        try
        {
            _details = await _service.GetAsync(
                _userId,
                _reviewId);

            SubmissionText.Text =
                $"{_details.ReportNumber} | Item {_details.ItemNumber}";

            InstructionsBox.Text = _details.CorrectionInstructions;
            DescriptionBox.Text = _details.Description;
            PackagingBox.SelectedItem = _details.Packaging;
            NotesBox.Text = _details.Notes;

            UpdateControls();
            DescriptionBox.Focus();
        }
        catch (InvalidOperationException ex)
        {
            SubmissionText.Text = "Submission unavailable.";
            ShowStatus(ex.Message, true);
        }
        catch (Exception)
        {
            SubmissionText.Text = "Submission unavailable.";

            ShowStatus(
                "The correction form could not be loaded. " +
                "Close it and refresh Intake Reviews.",
                true);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_saving)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    private void CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_saving)
        {
            DialogResult = false;
        }
    }

    private async void ResubmitButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_saving || _details is null)
        {
            return;
        }

        string description = DescriptionBox.Text.Trim();
        string notes = NotesBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(description))
        {
            ShowStatus("Enter the corrected description.", true);
            DescriptionBox.Focus();
            return;
        }

        if (PackagingBox.SelectedItem is not string packaging)
        {
            ShowStatus("Choose a packaging option.", true);
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Resubmit {_details.ReportNumber}, " +
            $"item {_details.ItemNumber}, for review?",
            "Resubmit Intake",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _saving = true;
        UpdateControls();
        StatusText.Visibility = Visibility.Collapsed;

        bool saved = false;

        try
        {
            await _service.ResubmitAsync(
                _userId,
                _reviewId,
                _details.Revision,
                description,
                packaging,
                notes);

            saved = true;
        }
        catch (DbUpdateConcurrencyException)
        {
            ShowStatus(
                "This submission changed. Close this window, " +
                "refresh Intake Reviews, and reopen it.",
                true);
        }
        catch (InvalidOperationException ex)
        {
            ShowStatus(ex.Message, true);
        }
        catch (Exception)
        {
            ShowStatus(
                "The correction could not be saved. " +
                "Your entries remain in this window. " +
                "Check the submission status before trying again.",
                true);
        }
        finally
        {
            _saving = false;
            UpdateControls();
        }

        if (saved)
        {
            DialogResult = true;
        }
    }

    private void UpdateControls()
    {
        bool canEdit = _details is not null && !_saving;

        FormPanel.IsEnabled = canEdit;
        ResubmitButton.IsEnabled = canEdit;
        CancelButton.IsEnabled = !_saving;
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