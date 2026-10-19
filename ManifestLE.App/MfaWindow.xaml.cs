using System;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.App.Views;

public partial class MfaWindow : Window
{
    private readonly Guid _userId;
    private readonly string _agencyName;
    private readonly MfaService _mfaService = new();

    private readonly DateTimeOffset _expiresAt =
        DateTimeOffset.UtcNow.AddMinutes(5);

    private byte[]? _secret;
    private bool _isEnrollment;
    private bool _isBusy;

    public AppUser? VerifiedUser { get; private set; }

    public MfaWindow(Guid userId, string agencyName)
    {
        InitializeComponent();

        _userId = userId;
        _agencyName = agencyName;

        Loaded += MfaWindow_Loaded;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isBusy)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_secret is not null)
        {
            CryptographicOperations.ZeroMemory(_secret);
            _secret = null;
        }

        QrImage.Source = null;
        ManualKeyBox.Clear();
        CodeBox.Clear();

        base.OnClosed(e);
    }

    private async void MfaWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= MfaWindow_Loaded;
        _isBusy = true;

        try
        {
            await using var database =
                DevelopmentDatabase.CreateContext();

            var credential = await database.UserCredentials
                .AsNoTracking()
                .Include(x => x.User)
                .SingleAsync(x => x.UserId == _userId);

            if (!credential.User.IsActive)
            {
                ShowError("This account is inactive.");
                return;
            }

            _isEnrollment = !credential.IsMfaEnabled;

            if (_isEnrollment)
            {
                _secret = _mfaService.GenerateSecret();

                InstructionsText.Text =
                    "Add a new account in your authenticator app. " +
                    "Scan this QR code, then enter its six-digit code.";

                ManualKeyBox.Text =
                    _mfaService.GetManualEntryKey(_secret);

                byte[] png = _mfaService.CreateQrCode(
                    _secret,
                    credential.User.Username,
                    _agencyName);

                try
                {
                    using var stream = new MemoryStream(png);

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    QrImage.Source = bitmap;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(png);
                }

                EnrollmentPanel.Visibility = Visibility.Visible;
                VerifyButton.Content = "Enable MFA and Sign In";
            }
            else
            {
                if (credential.ProtectedMfaSecret is null)
                {
                    ShowError(
                        "The MFA secret is missing. " +
                        "Contact an administrator.");

                    return;
                }

                _secret = _mfaService.UnprotectSecret(
                    credential.ProtectedMfaSecret);

                InstructionsText.Text =
                    "Enter the current six-digit code from " +
                    "your ManifestLE authenticator account.";
            }

            CodeBox.IsEnabled = true;
            VerifyButton.IsEnabled = true;
            CodeBox.Focus();
        }
        catch (Exception)
        {
            ShowError(
                "MFA could not initialize. Close this window " +
                "and contact an administrator.");
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async void VerifyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isBusy || _secret is null)
        {
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;

        if (DateTimeOffset.UtcNow >= _expiresAt)
        {
            CodeBox.IsEnabled = false;
            VerifyButton.IsEnabled = false;

            ShowError(
                "This sign-in attempt expired. " +
                "Close this window and sign in again.");

            return;
        }

        string code = CodeBox.Text.Trim();

        _isBusy = true;
        CodeBox.IsEnabled = false;
        VerifyButton.IsEnabled = false;

        try
        {
            await using var database =
                DevelopmentDatabase.CreateContext();

            await using var transaction =
                await database.Database.BeginTransactionAsync();

            var credential = await database.UserCredentials
                .Include(x => x.User)
                .SingleAsync(x => x.UserId == _userId);

            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (!credential.User.IsActive)
            {
                ShowError("This account is inactive.");
                return;
            }

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

            // Do not overwrite enrollment changed by another window.
            if (credential.IsMfaEnabled == _isEnrollment)
            {
                ShowError(
                    "MFA settings changed. Close this window " +
                    "and sign in again.");

                return;
            }

            if (!_isEnrollment)
            {
                byte[] currentSecret = _mfaService.UnprotectSecret(
                    credential.ProtectedMfaSecret
                    ?? throw new InvalidOperationException());

                bool secretMatches;

                try
                {
                    secretMatches =
                        CryptographicOperations.FixedTimeEquals(
                            _secret,
                            currentSecret);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(currentSecret);
                }

                if (!secretMatches)
                {
                    ShowError(
                        "MFA settings changed. Close this window " +
                        "and sign in again.");

                    return;
                }
            }

            bool valid = _mfaService.VerifyCode(
                _secret,
                code,
                _isEnrollment
                    ? null
                    : credential.LastAcceptedMfaTimeStep,
                out long matchedTimeStep);

            if (!valid)
            {
                credential.FailedLoginAttempts++;

                if (credential.FailedLoginAttempts >= 5)
                {
                    credential.LockoutUntil = now.AddMinutes(15);
                }

                database.AuditLogEntries.Add(new AuditLogEntry
                {
                    UserId = _userId,
                    Action = "Authentication.MfaRejected",
                    EntityType = nameof(AppUser),
                    EntityId = _userId,
                    Details = "Authenticator verification failed."
                });

                await database.SaveChangesAsync();
                await transaction.CommitAsync();

                ShowError(
                    "Code invalid or already used. " +
                    "Try a fresh code from your authenticator.");

                return;
            }

            if (_isEnrollment)
            {
                credential.ProtectedMfaSecret =
                    _mfaService.ProtectSecret(_secret);

                credential.IsMfaEnabled = true;

                database.AuditLogEntries.Add(new AuditLogEntry
                {
                    UserId = _userId,
                    Action = "Authentication.MfaEnabled",
                    EntityType = nameof(AppUser),
                    EntityId = _userId,
                    Details = "Authenticator enrollment confirmed."
                });
            }

            credential.LastAcceptedMfaTimeStep = matchedTimeStep;
            credential.FailedLoginAttempts = 0;
            credential.LockoutUntil = null;

            database.AuditLogEntries.Add(new AuditLogEntry
            {
                UserId = _userId,
                Action = "Authentication.SignedIn",
                EntityType = nameof(AppUser),
                EntityId = _userId,
                Details = "Password and authenticator sign-in completed."
            });

            await database.SaveChangesAsync();
            await transaction.CommitAsync();

            VerifiedUser = credential.User;

            _isBusy = false;
            DialogResult = true;
        }
        catch (Exception)
        {
            ShowError(
                "MFA verification could not be completed. " +
                "Close this window and sign in again.");
        }
        finally
        {
            _isBusy = false;
            CodeBox.Clear();
            CodeBox.IsEnabled = true;
            VerifyButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}