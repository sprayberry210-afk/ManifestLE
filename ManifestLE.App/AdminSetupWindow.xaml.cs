using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.App.Views;

public partial class AdminSetupWindow : Window
{
    private readonly PasswordService _passwordService = new();
    private bool _isSaving;

    public AdminSetupWindow()
    {
        InitializeComponent();

        Loaded += (_, _) => DisplayNameBox.Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isSaving)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        PasswordInput.Clear();
        ConfirmPasswordInput.Clear();

        base.OnClosed(e);
    }

    private async void CreateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSaving)
        {
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;

        string displayName = DisplayNameBox.Text.Trim();
        string username = UsernameBox.Text.Trim().ToLowerInvariant();
        string password = PasswordInput.Password;
        string confirmation = ConfirmPasswordInput.Password;

        if (string.IsNullOrWhiteSpace(displayName))
        {
            ShowError("Enter a display name.");
            DisplayNameBox.Focus();
            return;
        }

        if (!Regex.IsMatch(username, @"\A[a-z0-9._-]{3,50}\z"))
        {
            ShowError(
                "Use a username of 3–50 characters containing " +
                "letters, numbers, periods, underscores, or hyphens.");

            UsernameBox.Focus();
            return;
        }

        if (password.Length < 15 || password.Length > 128)
        {
            ShowError("Use a password between 15 and 128 characters.");
            PasswordInput.Focus();
            return;
        }

        if (!string.Equals(
            password,
            confirmation,
            StringComparison.Ordinal))
        {
            ShowError("The passwords do not match.");
            ConfirmPasswordInput.Focus();
            return;
        }

        _isSaving = true;
        CreateButton.IsEnabled = false;
        FormPanel.IsEnabled = false;
        CreateButton.Content = "Creating…";

        try
        {
            await using var database =
                DevelopmentDatabase.CreateContext();

            await using var transaction =
                await database.Database.BeginTransactionAsync();

            if (!await database.Agencies.AnyAsync())
            {
                ShowError("Complete agency setup first.");
                return;
            }

            // Initial setup is allowed only before any users exist.
            if (await database.Users.AnyAsync())
            {
                ShowError(
                    "Initial account setup is already complete. " +
                    "Close this window and restart ManifestLE.");

                return;
            }

            var user = new AppUser
            {
                Username = username,
                DisplayName = displayName,
                Role = UserRole.Administrator,
                IsActive = true
            };

            var credential = new UserCredential
            {
                UserId = user.Id,
                User = user,
                PasswordHash = _passwordService.HashPassword(
                    user,
                    password)
            };

            database.Users.Add(user);
            database.UserCredentials.Add(credential);

            database.AuditLogEntries.Add(new AuditLogEntry
            {
                UserId = user.Id,
                Action = "User.InitialAdministratorCreated",
                EntityType = nameof(AppUser),
                EntityId = user.Id,
                Details = "First administrator created during setup."
            });

            await database.SaveChangesAsync();
            await transaction.CommitAsync();

            PasswordInput.Clear();
            ConfirmPasswordInput.Clear();

            _isSaving = false;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError($"Could not create the administrator. {ex.Message}");
        }
        finally
        {
            _isSaving = false;
            CreateButton.IsEnabled = true;
            FormPanel.IsEnabled = true;
            CreateButton.Content = "Create Administrator";
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}