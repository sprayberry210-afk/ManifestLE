using System;
using System.ComponentModel;
using System.Windows;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.App.Views;

public partial class AgencySetupWindow : Window
{
    private bool _isSaving;

    public AgencySetupWindow()
    {
        InitializeComponent();

        Loaded += (_, _) => AgencyNameBox.Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isSaving)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    private async void SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSaving)
        {
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;

        string name = AgencyNameBox.Text.Trim();
        string code = AgencyCodeBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Enter the agency name.");
            AgencyNameBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            ShowError("Enter the agency code.");
            AgencyCodeBox.Focus();
            return;
        }

        _isSaving = true;
        SaveButton.IsEnabled = false;
        FormPanel.IsEnabled = false;
        SaveButton.Content = "Saving…";

        try
        {
            await using var database =
                DevelopmentDatabase.CreateContext();

            await using var transaction =
                await database.Database.BeginTransactionAsync();

            if (await database.Agencies.AnyAsync())
            {
                ShowError("An agency is already configured.");
                return;
            }

            var agency = new Agency
            {
                Name = name,
                AgencyCode = code,
                Address = AddressBox.Text.Trim(),
                PhoneNumber = PhoneBox.Text.Trim()
            };

            database.Agencies.Add(agency);

            database.AuditLogEntries.Add(new AuditLogEntry
            {
                Action = "Agency.Created",
                EntityType = nameof(Agency),
                EntityId = agency.Id,
                Details = "Agency configured during initial setup."
            });

            await database.SaveChangesAsync();
            await transaction.CommitAsync();

            _isSaving = false;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError($"Could not save the agency. {ex.Message}");
        }
        finally
        {
            _isSaving = false;
            SaveButton.IsEnabled = true;
            FormPanel.IsEnabled = true;
            SaveButton.Content = "Save Agency";
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}