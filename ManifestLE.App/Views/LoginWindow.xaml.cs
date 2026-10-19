using System;
using System.ComponentModel;
using System.Windows;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.App.Views;

public partial class LoginWindow : Window
{
    private readonly PasswordService _passwordService = new();
    private readonly string _agencyName;
    private bool _isSigningIn;

    public AppUser? SignedInUser { get; private set; }

    public LoginWindow(string agencyName)
    {
        InitializeComponent();

        _agencyName = agencyName;
        AgencyNameText.Text = agencyName;

        Loaded += (_, _) => UsernameBox.Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isSigningIn)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        PasswordInput.Clear();
        base.OnClosed(e);
    }

    private async void SignInButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSigningIn)
        {
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;

        string username = UsernameBox.Text.Trim().ToLowerInvariant();
        string password = PasswordInput.Password;

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrEmpty(password))
        {
            ShowError("Enter your username and password.");
            return;
        }

        _isSigningIn = true;
        SignInButton.IsEnabled = false;
        FormPanel.IsEnabled = false;
        SignInButton.Content = "Signing in…";

        try
        {
            Guid verifiedUserId;

            // Finish the password transaction before opening MFA.
            // This avoids holding a database lock while you enter a code.
            await using (var database =
                DevelopmentDatabase.CreateContext())
            {
                await using var transaction =
                    await database.Database.BeginTransactionAsync();

                var credential = await database.UserCredentials
                    .Include(x => x.User)
                    .SingleOrDefaultAsync(
                        x => x.User.Username == username);

                if (credential is null || !credential.User.IsActive)
                {
                    database.AuditLogEntries.Add(new AuditLogEntry
                    {
                        Action = "Authentication.PasswordRejected",
                        EntityType = nameof(AppUser),
                        Details = "Sign-in attempt rejected."
                    });

                    await database.SaveChangesAsync();
                    await transaction.CommitAsync();

                    ShowError(
                        "Sign-in failed. Check your credentials " +
                        "or contact an administrator.");

                    return;
                }

                DateTimeOffset now = DateTimeOffset.UtcNow;

                if (credential.LockoutUntil is DateTimeOffset until &&
                    until > now)
                {
                    ShowError(
                        "Sign-in is temporarily locked. Try again later.");

                    return;
                }

                if (credential.LockoutUntil is not null)
                {
                    credential.LockoutUntil = null;
                    credential.FailedLoginAttempts = 0;
                }

                var result = _passwordService.VerifyPassword(
                    credential.User,
                    credential.PasswordHash,
                    password);

                if (result == PasswordVerificationResult.Failed)
                {
                    credential.FailedLoginAttempts++;

                    if (credential.FailedLoginAttempts >= 5)
                    {
                        credential.LockoutUntil = now.AddMinutes(15);
                    }

                    database.AuditLogEntries.Add(new AuditLogEntry
                    {
                        UserId = credential.UserId,
                        Action = "Authentication.PasswordRejected",
                        EntityType = nameof(AppUser),
                        EntityId = credential.UserId,
                        Details = "Password verification failed."
                    });

                    await database.SaveChangesAsync();
                    await transaction.CommitAsync();

                    ShowError(
                        "Sign-in failed. Check your credentials " +
                        "or contact an administrator.");

                    return;
                }

                if (result ==
                    PasswordVerificationResult.SuccessRehashNeeded)
                {
                    credential.PasswordHash =
                        _passwordService.HashPassword(
                            credential.User,
                            password);
                }

                // Failed attempts reset only after successful MFA.
                database.AuditLogEntries.Add(new AuditLogEntry
                {
                    UserId = credential.UserId,
                    Action = "Authentication.PasswordVerified",
                    EntityType = nameof(AppUser),
                    EntityId = credential.UserId,
                    Details = "Password verified; MFA pending."
                });

                await database.SaveChangesAsync();
                await transaction.CommitAsync();

                verifiedUserId = credential.UserId;
            }

            PasswordInput.Clear();
            password = string.Empty;

            var mfaWindow = new MfaWindow(
                verifiedUserId,
                _agencyName)
            {
                Owner = this
            };

            if (mfaWindow.ShowDialog() != true ||
                mfaWindow.VerifiedUser is null)
            {
                ShowError(
                    "Authenticator verification was not completed. " +
                    "Enter your password to try again.");

                return;
            }

            SignedInUser = mfaWindow.VerifiedUser;

            _isSigningIn = false;
            DialogResult = true;
        }
        catch (Exception)
        {
            ShowError(
                "Sign-in could not be completed. " +
                "Close ManifestLE and try again.");
        }
        finally
        {
            PasswordInput.Clear();

            _isSigningIn = false;
            SignInButton.IsEnabled = true;
            FormPanel.IsEnabled = true;
            SignInButton.Content = "Sign In";
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}