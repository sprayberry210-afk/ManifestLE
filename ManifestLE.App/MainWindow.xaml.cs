using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ManifestLE.App.Views;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.App;

public partial class MainWindow : Window
{
    private AppUser? _currentUser;

    public MainWindow()
    {
        InitializeComponent();

        IsEnabled = false;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;

        try
        {
            bool agencyExists;

            await using (var database =
                DevelopmentDatabase.CreateContext())
            {
                agencyExists = await database.Agencies.AnyAsync();
            }

            if (!agencyExists)
            {
                var agencySetup = new AgencySetupWindow
                {
                    Owner = this
                };

                if (agencySetup.ShowDialog() != true)
                {
                    Close();
                    return;
                }
            }

            bool usersExist;

            await using (var database =
                DevelopmentDatabase.CreateContext())
            {
                usersExist = await database.Users.AnyAsync();
            }

            if (!usersExist)
            {
                var adminSetup = new AdminSetupWindow
                {
                    Owner = this
                };

                if (adminSetup.ShowDialog() != true)
                {
                    Close();
                    return;
                }
            }

            Agency agency;

            await using (var database =
                DevelopmentDatabase.CreateContext())
            {
                agency = await database.Agencies
                    .AsNoTracking()
                    .SingleAsync();
            }

            var loginWindow = new LoginWindow(agency.Name)
            {
                Owner = this
            };

            if (loginWindow.ShowDialog() != true ||
                loginWindow.SignedInUser is null)
            {
                Close();
                return;
            }

            _currentUser = loginWindow.SignedInUser;

            SignedInNameText.Text = _currentUser.DisplayName;

            Title =
                $"ManifestLE — {agency.Name} | " +
                $"{_currentUser.DisplayName} | Development";

            ApplyNavigationPermissions();

            DashboardWelcomeText.Text =
                $"Signed in to {agency.Name}. " +
                "Use Item Intake to submit property and " +
                "the registers to inspect records.";

            if (CanAccessAudits())
            {
                DashboardWelcomeText.Text +=
                    " Use Chain of Custody to record movements, " +
                    "Inventory Audits to verify stored property, " +
                    "and Audit History to review application activity.";
            }

            if (IsAdministrator())
            {
                DashboardWelcomeText.Text +=
                    " Use User Administration to create individual accounts.";
            }

            ShowDashboard();
            IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"ManifestLE could not initialize.\n\n{ex.Message}",
                "ManifestLE",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Close();
        }
    }

    private void ApplyNavigationPermissions()
    {
        bool canAccessAudits = CanAccessAudits();
        bool isAdministrator = IsAdministrator();

        AuditHistoryButton.IsEnabled = canAccessAudits;
        AuditHistoryButton.Visibility = canAccessAudits
            ? Visibility.Visible
            : Visibility.Collapsed;

        InventoryAuditsButton.IsEnabled = canAccessAudits;
        InventoryAuditsButton.Visibility = canAccessAudits
            ? Visibility.Visible
            : Visibility.Collapsed;

        UserAdministrationButton.IsEnabled = isAdministrator;
        UserAdministrationButton.Visibility = isAdministrator
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private bool CanAccessAudits()
    {
        return _currentUser is { IsActive: true } &&
               (_currentUser.Role == UserRole.Administrator ||
                _currentUser.Role == UserRole.EvidenceCustodian);
    }

    private bool IsAdministrator()
    {
        return _currentUser is
        {
            IsActive: true,
            Role: UserRole.Administrator
        };
    }

    private void DashboardButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_currentUser is null)
        {
            return;
        }

        ShowDashboard();
    }

    private void IntakeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_currentUser is null)
        {
            return;
        }

        WorkspaceContent.Content =
            new EvidenceIntakeView(_currentUser.Id);

        SetActiveNavigation(IntakeButton);
    }

    private void EvidenceRecordsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenRegister(
            PropertyRegisterType.Evidence,
            EvidenceRecordsButton);
    }

    private void FoundPropertyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenRegister(
            PropertyRegisterType.FoundProperty,
            FoundPropertyButton);
    }

    private void SafekeepingButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenRegister(
            PropertyRegisterType.Safekeeping,
            SafekeepingButton);
    }

    private void OpenRegister(
        PropertyRegisterType register,
        Button navigationButton)
    {
        if (_currentUser is null)
        {
            return;
        }

        WorkspaceContent.Content =
            new PropertyRegisterView(_currentUser.Id, register);

        SetActiveNavigation(navigationButton);
    }

    private void ChainOfCustodyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_currentUser is null)
        {
            return;
        }

        WorkspaceContent.Content =
            new ChainOfCustodyView(_currentUser.Id);

        SetActiveNavigation(ChainOfCustodyButton);
    }

    private void StorageLocationsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_currentUser is null)
        {
            return;
        }

        WorkspaceContent.Content =
            new StorageLocationsView(_currentUser.Id);

        SetActiveNavigation(StorageLocationsButton);
    }

    private void InventoryAuditsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!CanAccessAudits())
        {
            return;
        }

        WorkspaceContent.Content =
            new InventoryAuditsView(_currentUser!.Id);

        SetActiveNavigation(InventoryAuditsButton);
    }

    private void AuditHistoryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!CanAccessAudits())
        {
            return;
        }

        WorkspaceContent.Content =
            new AuditHistoryView(_currentUser!.Id);

        SetActiveNavigation(AuditHistoryButton);
    }

    private void UserAdministrationButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsAdministrator())
        {
            return;
        }

        WorkspaceContent.Content =
            new UserAdministrationView(_currentUser!.Id);

        SetActiveNavigation(UserAdministrationButton);
    }

    private void ShowDashboard()
    {
        WorkspaceContent.Content = DashboardPanel;
        SetActiveNavigation(DashboardButton);
    }

    private void SetActiveNavigation(Button activeButton)
    {
        Button[] buttons =
        {
            DashboardButton,
            IntakeButton,
            EvidenceRecordsButton,
            FoundPropertyButton,
            SafekeepingButton,
            ChainOfCustodyButton,
            StorageLocationsButton,
            InventoryAuditsButton,
            AuditHistoryButton,
            UserAdministrationButton
        };

        foreach (var button in buttons)
        {
            button.Background = Brushes.Transparent;
        }

        activeButton.Background =
            new SolidColorBrush(Color.FromRgb(20, 121, 209));
    }
}