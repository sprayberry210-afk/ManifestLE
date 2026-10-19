using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Services;

namespace ManifestLE.App.Views;

public partial class UserAdministrationView : UserControl
{
    private readonly Guid _administratorId;
    private readonly UserAdministrationService _service = new();

    private bool _busy;
    private bool _authorized;
    private AccountRow? _editingAccount;

    public UserAdministrationView()
        : this(Guid.Empty)
    {
    }

    public UserAdministrationView(Guid administratorId)
    {
        InitializeComponent();

        _administratorId = administratorId;

        RoleBox.ItemsSource = new[]
        {
            new RoleChoice(UserRole.Officer, "Officer"),
            new RoleChoice(
                UserRole.EvidenceCustodian,
                "Evidence Custodian"),
            new RoleChoice(UserRole.Administrator, "Administrator")
        };

        RoleBox.SelectedIndex = 0;

        Loaded += UserAdministrationView_Loaded;

        Unloaded += (_, _) =>
        {
            PasswordInput.Clear();
            ConfirmPasswordInput.Clear();
        };
    }

    private async void UserAdministrationView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= UserAdministrationView_Loaded;
        await RefreshAsync();
    }

    private async Task LoadAccountsAsync()
    {
        _authorized = false;
        ClearEditor();
        UsersGrid.ItemsSource = null;
        UpdateControls();

        var users = await _service.GetUsersAsync(_administratorId);

        UsersGrid.ItemsSource = users.Select(x => new AccountRow(
            x.Id,
            x.DisplayName,
            x.Username,
            x.BadgeNumber,
            x.Role switch
            {
                UserRole.Officer => "Officer",
                UserRole.EvidenceCustodian => "Evidence Custodian",
                UserRole.Administrator => "Administrator",
                _ => "Unknown"
            },
            x.IsActive ? "Active" : "Inactive"
        )).ToList();

        _authorized = true;
    }

    private async Task RefreshAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UpdateControls();

        try
        {
            await LoadAccountsAsync();
            ShowStatus("Accounts loaded.", false);
        }
        catch (InvalidOperationException ex)
        {
            ShowStatus(ex.Message, true);
        }
        catch (Exception)
        {
            ShowStatus(
                "Accounts could not be loaded. Reopen this screen.",
                true);
        }
        finally
        {
            _busy = false;
            UpdateControls();
        }
    }

    private async void RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void UsersGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_busy || !_authorized)
        {
            return;
        }

        if (UsersGrid.SelectedItem is not AccountRow account)
        {
            ClearEditor();
            UpdateControls();
            return;
        }

        _editingAccount = account;

        EditingAccountText.Text =
            $"Username: {account.Username} | " +
            $"Role: {account.Role}";

        EditDisplayNameBox.Text = account.DisplayName;
        EditBadgeNumberBox.Text = account.BadgeNumber;

        EditorCard.Visibility = Visibility.Visible;
        UpdateControls();
    }

    private void CancelEditButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        UsersGrid.SelectedItem = null;
        ClearEditor();
        UpdateControls();
    }

    private async void SaveDetailsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy || !_authorized || _editingAccount is null)
        {
            return;
        }

        var account = _editingAccount;

        string displayName = EditDisplayNameBox.Text;
        string badgeNumber = EditBadgeNumberBox.Text;

        _busy = true;
        UpdateControls();

        try
        {
            await _service.UpdateUserDetailsAsync(
                _administratorId,
                account.Id,
                displayName,
                badgeNumber,
                account.DisplayName,
                account.BadgeNumber);

            await ReloadAfterSaveAsync(
                "Account details saved. Changes are recorded " +
                "when the values differ from the original details.");
        }
        catch (InvalidOperationException ex)
        {
            ShowStatus(ex.Message, true);
        }
        catch (Exception)
        {
            ShowStatus(
                "The details could not be saved. Refresh the " +
                "account list before trying again.",
                true);
        }
        finally
        {
            _busy = false;
            UpdateControls();
        }
    }

    private async void CreateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy || !_authorized)
        {
            return;
        }

        StatusText.Visibility = Visibility.Collapsed;

        if (RoleBox.SelectedItem is not RoleChoice role)
        {
            ShowStatus("Choose a role.", true);
            return;
        }

        string password = PasswordInput.Password;

        if (!string.Equals(
            password,
            ConfirmPasswordInput.Password,
            StringComparison.Ordinal))
        {
            ShowStatus("The passwords do not match.", true);
            return;
        }

        if (role.Value == UserRole.Administrator)
        {
            var answer = MessageBox.Show(
                "This account will have full administrative access. " +
                "Create the administrator account?",
                "Confirm Administrator Role",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        string username = UsernameBox.Text;
        string displayName = DisplayNameBox.Text;
        string badgeNumber = BadgeNumberBox.Text;

        _busy = true;
        UpdateControls();

        try
        {
            await _service.CreateUserAsync(
                _administratorId,
                username,
                displayName,
                password,
                role.Value,
                badgeNumber);

            PasswordInput.Clear();
            ConfirmPasswordInput.Clear();
            UsernameBox.Clear();
            DisplayNameBox.Clear();
            BadgeNumberBox.Clear();
            RoleBox.SelectedIndex = 0;

            await ReloadAfterSaveAsync(
                "Account created. The user will enroll their " +
                "authenticator when they first sign in.");
        }
        catch (InvalidOperationException ex)
        {
            ShowStatus(ex.Message, true);
        }
        catch (Exception)
        {
            ShowStatus(
                "The account could not be created. Refresh the " +
                "account list before trying again.",
                true);
        }
        finally
        {
            password = string.Empty;
            PasswordInput.Clear();
            ConfirmPasswordInput.Clear();

            _busy = false;
            UpdateControls();
        }
    }

    private async Task ReloadAfterSaveAsync(string successMessage)
    {
        try
        {
            await LoadAccountsAsync();
            ShowStatus(successMessage, false);
        }
        catch (Exception)
        {
            _authorized = false;
            UsersGrid.ItemsSource = null;
            ClearEditor();

            ShowStatus(
                "The save completed, but the account list could " +
                "not refresh. Reopen this screen before continuing.",
                true);
        }
    }

    private void ClearEditor()
    {
        _editingAccount = null;

        if (EditorCard is null)
        {
            return;
        }

        EditorCard.Visibility = Visibility.Collapsed;
        EditingAccountText.Text = string.Empty;
        EditDisplayNameBox.Clear();
        EditBadgeNumberBox.Clear();
    }

    private void UpdateControls()
    {
        bool enabled = _authorized && !_busy;
        bool canEdit = enabled && _editingAccount is not null;

        FormPanel.IsEnabled = enabled;
        CreateButton.IsEnabled = enabled;
        UsersGrid.IsEnabled = enabled;
        RefreshButton.IsEnabled = !_busy;

        EditFormPanel.IsEnabled = canEdit;
        SaveDetailsButton.IsEnabled = canEdit;
        CancelEditButton.IsEnabled = canEdit;
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(255, 155, 155))
            : new SolidColorBrush(Color.FromRgb(134, 239, 172));

        StatusText.Visibility = Visibility.Visible;
    }

    private sealed record RoleChoice(UserRole Value, string Name);

    private sealed record AccountRow(
        Guid Id,
        string DisplayName,
        string Username,
        string BadgeNumber,
        string Role,
        string Status)
    {
        public string BadgeDisplay =>
            string.IsNullOrWhiteSpace(BadgeNumber)
                ? "—"
                : BadgeNumber;
    }
}