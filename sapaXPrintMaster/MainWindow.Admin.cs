using System;
using System.Windows;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.SqlClient;
using sapaXPrintCore.Jobs;
using sapaXPrintCore.Models;
using sapaXPrintCore.Printers;
using sapaXPrintMaster.Services;
using sapaXPrintCore.Settings;

namespace sapaXPrintMaster
{
    // Administrator tab.
    //   SuperAdmin    : everything (DB connection, tables, Administrators and Users, companies, sub areas, agent + printers)
    //   Administrator : adds standard Users only; no DB connection, organization or agent changes
    //   User          : no access to this tab (can still print)
    public partial class MainWindow
    {
        private LoggedInUser? _currentUser;

        private bool IsLoggedIn => _currentUser != null;
        private bool IsSuperAdmin => _currentUser?.Role == UsersService.SuperAdminRole;
        private bool CanAdminister => _currentUser?.Role is UsersService.SuperAdminRole or UsersService.AdminRole;

        // ------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------
        private static void Say(TextBlock t, string msg, bool ok)
        {
            t.Visibility = Visibility.Visible;
            t.Foreground = ok ? Brushes.SeaGreen : Brushes.Firebrick;
            t.Text = (ok ? "✔ " : "✘ ") + msg;
        }

        private static void Info(TextBlock t, string msg)
        {
            t.Visibility = Visibility.Visible;
            t.Foreground = Brushes.Gray;
            t.Text = msg;
        }

        private void LoadDbFields()
        {
            var s = DbConfig.Load();
            if (s == null) return;
            txtDbServer.Text = s.Server;
            txtDbName.Text = s.Database;
            chkWindowsAuth.IsChecked = s.UseWindowsAuth;
            txtDbUser.Text = s.User;
        }

        // ------------------------------------------------------------
        // State machine. Before sign-in the order is:
        //   1. Database connection (hidden once this PC is registered as an agent)
        //   2. Users table
        //   3. First SuperAdmin
        //   4. Sign in
        // ------------------------------------------------------------
        private async void CheckFirstRunAsync() => await RefreshAdminStateAsync();

        private async Task RefreshAdminStateAsync()
        {
            LoadDbFields();

            if (IsLoggedIn)
            {
                await ShowLoggedInStateAsync();
                return;
            }

            pnlAdmin.Visibility = Visibility.Collapsed;
            pnlLogin.Visibility = Visibility.Collapsed;
            pnlCreateAdmin.Visibility = Visibility.Collapsed;
            pnlTablesStep.Visibility = Visibility.Collapsed;

            // No saved connection: the form is the only thing to show.
            if (!DbConfig.IsConfigured)
            {
                pnlDbConnection.Visibility = Visibility.Visible;
                Info(txtDbStatus, "Enter the database connection and click Test & Save.");
                return;
            }

            try
            {
                // Connection works: hide the form before sign-in.
                // SuperAdmin can still change it after signing in.
                await AdminDbService.TableExistsAsync("Users");
                pnlDbConnection.Visibility = Visibility.Collapsed;

                await RefreshTablesStepAsync();
                if (pnlTablesStep.Visibility == Visibility.Visible)
                    return;                               // Users table still missing

                if (!await UsersService.SuperAdminExistsAsync())
                {
                    pnlCreateAdmin.Visibility = Visibility.Visible;
                    return;
                }

                pnlLogin.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                // Connection failed: show the form with the reason.
                pnlDbConnection.Visibility = Visibility.Visible;
                Say(txtDbStatus, AdminDbService.FriendlyError(ex), false);
            }
        }

        private async Task ShowLoggedInStateAsync()
        {
            pnlCreateAdmin.Visibility = Visibility.Collapsed;
            pnlLogin.Visibility = Visibility.Collapsed;
            pnlAdmin.Visibility = Visibility.Visible;
            txtLoggedInAs.Text = $"Signed in as {_currentUser!.UserId} ({_currentUser.Role})";

            bool su = IsSuperAdmin;

            tabsAdmin.Visibility = CanAdminister ? Visibility.Visible : Visibility.Collapsed;
            pnlNoAccess.Visibility = CanAdminister ? Visibility.Collapsed : Visibility.Visible;

            pnlDbConnection.Visibility = su ? Visibility.Visible : Visibility.Collapsed;
            tabOrg.Visibility = su ? Visibility.Visible : Visibility.Collapsed;
            pnlRegister.Visibility = su ? Visibility.Visible : Visibility.Collapsed;
            txtAgentLocked.Visibility = su ? Visibility.Collapsed : Visibility.Visible;

            cmbNewUserRole.ItemsSource = su
                ? new[] { UsersService.UserRole, UsersService.AdminRole }
                : new[] { UsersService.UserRole };
            cmbNewUserRole.SelectedIndex = 0;

            if (!CanAdminister) return;

            try
            {
                await RefreshTablesStepAsync();
                await LoadAdminDataAsync();
                await LoadRegisteredAsync();
            }
            catch (Exception ex)
            {
                Say(txtRegisterStatus, AdminDbService.FriendlyError(ex), false);
            }
        }

        // Shows the "Create Tables" step only when something is missing.
        // Before sign-in: only Users can be created. After sign-in: SuperAdmin creates the rest.
        private async Task RefreshTablesStepAsync()
        {
            if (IsLoggedIn && !IsSuperAdmin)
            {
                pnlTablesStep.Visibility = Visibility.Collapsed;
                return;
            }

            var missing = await AdminDbService.GetMissingTableNamesAsync();
            var allowed = SchemaTemplates.Available(IsLoggedIn).Where(n => missing.Contains(n)).ToList();

            cmbTemplateTable.ItemsSource = allowed;
            cmbTemplateTable.SelectedIndex = allowed.Count > 0 ? 0 : -1;
            btnCreateAll.Visibility = IsLoggedIn && allowed.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            pnlTablesStep.Visibility = allowed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ------------------------------------------------------------
        // Step 1: database connection (SuperAdmin only after sign-in)
        // ------------------------------------------------------------
        private void chkWindowsAuth_Changed(object sender, RoutedEventArgs e)
        {
            if (pnlSqlAuth == null) return;
            pnlSqlAuth.Visibility = chkWindowsAuth.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void SaveDb_Click(object sender, RoutedEventArgs e)
        {
            if (IsLoggedIn && !IsSuperAdmin) return;

            var saved = DbConfig.Load();
            var s = new DbSettings
            {
                Server = txtDbServer.Text.Trim(),
                Database = txtDbName.Text.Trim(),
                UseWindowsAuth = chkWindowsAuth.IsChecked == true,
                User = txtDbUser.Text.Trim(),
                Password = string.IsNullOrEmpty(pwdDb.Password) ? (saved?.Password ?? "") : pwdDb.Password
            };

            btnSaveDb.IsEnabled = false;
            Info(txtDbStatus, "Testing connection...");

            try
            {
                await AdminDbService.TestAsync(s);
                DbConfig.Save(s);                 // saved only after a successful test
                pwdDb.Clear();
                await RefreshAdminStateAsync();
                Say(txtDbStatus, "Connection saved.", true);
            }
            catch (Exception ex)
            {
                Say(txtDbStatus, AdminDbService.FriendlyError(ex), false);
            }
            finally
            {
                btnSaveDb.IsEnabled = true;
            }
        }

        // ------------------------------------------------------------
        // Step 2: tables
        // ------------------------------------------------------------
        private async void CreateTable_Click(object sender, RoutedEventArgs e)
        {
            if (cmbTemplateTable.SelectedItem is not string name) return;
            if (IsLoggedIn && !IsSuperAdmin) return;

            btnCreateTable.IsEnabled = false;
            try
            {
                await AdminDbService.CreateTableAsync(name);
                Say(txtTablesStatus, $"Table {name} created.", true);
                await RefreshAdminStateAsync();
            }
            catch (Exception ex)
            {
                Say(txtTablesStatus, AdminDbService.FriendlyError(ex), false);
            }
            finally
            {
                btnCreateTable.IsEnabled = true;
            }
        }

        private async void CreateAll_Click(object sender, RoutedEventArgs e)
        {
            if (!IsSuperAdmin) return;
            try
            {
                var created = await AdminDbService.CreateAllMissingAsync();
                Say(txtTablesStatus,
                    created.Count == 0 ? "Nothing to create." : "Created: " + string.Join(", ", created),
                    true);
                await RefreshAdminStateAsync();
            }
            catch (Exception ex)
            {
                Say(txtTablesStatus, AdminDbService.FriendlyError(ex), false);
            }
        }

        private async Task LoadTablesListAsync()
        {
            var tables = await AdminDbService.ListTablesAsync();
            dgTables.ItemsSource = tables;
            txtTableCount.Text = $"{tables.Count} table(s)";
        }

        private async void RefreshTables_Click(object sender, RoutedEventArgs e)
        {
            try { await LoadTablesListAsync(); }
            catch (Exception ex) { MessageBox.Show(AdminDbService.FriendlyError(ex), "Error"); }
        }

        // ------------------------------------------------------------
        // Step 3: first SuperAdmin
        // ------------------------------------------------------------
        private async void CreateFirstAdmin_Click(object sender, RoutedEventArgs e)
        {
            Info(txtCreateAdminStatus, "Creating account...");

            var (ok, message) = await UsersService.CreateFirstAdminAsync(
                txtNewAdminUserId.Text,
                pwdNewAdminPassword.Password,
                txtNewAdminUserName.Text,
                txtNewAdminEmail.Text);

            if (!ok)
            {
                Say(txtCreateAdminStatus, message, false);
                return;
            }

            pwdNewAdminPassword.Clear();
            await RefreshAdminStateAsync();      // next: sign in
        }

        // ------------------------------------------------------------
        // Step 4: sign in / sign out
        // ------------------------------------------------------------
        private void pwdPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) TryLogin();
        }

        private void Login_Click(object sender, RoutedEventArgs e) => TryLogin();

        private async void TryLogin()
        {
            txtLoginError.Visibility = Visibility.Collapsed;

            var (result, user, message) = await UsersService.LoginAsync(txtUsername.Text, pwdPassword.Password);
            pwdPassword.Clear();

            if (result != LoginResult.Success || user == null)
            {
                txtLoginError.Text = message;
                txtLoginError.Visibility = Visibility.Visible;
                return;
            }

            _currentUser = user;
            txtUsername.Clear();
            await RefreshAdminStateAsync();
        }

        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            _currentUser = null;
            pwdPassword.Clear();
            await RefreshAdminStateAsync();
        }

        // ------------------------------------------------------------
        // After sign-in: load the admin data
        // ------------------------------------------------------------
        private async Task LoadAdminDataAsync()
        {
            string computerId = ComputerIdentityService.GetComputerId();
            txtComputerId.Text = computerId;

            var creds = AgentCredentialStore.Load();
            txtAgentName.Text = creds?.AgentName is { Length: > 0 } n ? n : Environment.MachineName;
            txtPollSeconds.Text = AppSettings.PollIntervalSeconds.ToString();
            txtPollStatus.Text = "";

            await LoadTablesListAsync();
            await LoadRegisteredAsync();

            if (IsSuperAdmin)
                await ReloadCompaniesAsync();

            bool registered = await AdminDbService.IsComputerRegisteredAsync(computerId);
            if (registered)
                Say(txtRegisterStatus, "This computer is registered as an agent.", true);
            else
                Info(txtRegisterStatus, "This computer is not registered as an agent yet.");
        }

        // ------------------------------------------------------------
        // Users (Administrator: standard users only; SuperAdmin: also Administrators)
        // ------------------------------------------------------------
        private async void AddUser_Click(object sender, RoutedEventArgs e)
        {
            if (_currentUser == null) return;

            if (cmbNewUserRole.SelectedItem is not string role)
            {
                Say(txtAddUserStatus, "Choose a role.", false);
                return;
            }

            var (ok, message) = await UsersService.AddUserAsync(
                _currentUser.Role,
                role,
                txtAddUserId.Text,
                pwdAddUserPassword.Password,
                txtAddUserName.Text,
                txtAddUserEmail.Text);

            Say(txtAddUserStatus, message, ok);

            if (ok)
            {
                txtAddUserId.Clear();
                pwdAddUserPassword.Clear();
                txtAddUserName.Clear();
                txtAddUserEmail.Clear();
            }
        }

        // ------------------------------------------------------------
        // Organization (SuperAdmin only): companies and sub areas
        // ------------------------------------------------------------
        private async Task ReloadCompaniesAsync()
        {
            var companies = await AdminDbService.GetCompaniesAsync();
            var keepMgr = (cmbMgrCompany.SelectedItem as CompanyRow)?.CompanyID;
            var keepReg = (cmbCompany.SelectedItem as CompanyRow)?.CompanyID;

            cmbMgrCompany.ItemsSource = companies;
            cmbCompany.ItemsSource = companies;

            cmbMgrCompany.SelectedItem = companies.FirstOrDefault(c => c.CompanyID == keepMgr) ?? companies.FirstOrDefault();
            cmbCompany.SelectedItem = companies.FirstOrDefault(c => c.CompanyID == keepReg) ?? companies.FirstOrDefault();
        }

        private async void cmbCompany_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await LoadSubAreasAsync(cmbCompany.SelectedItem as CompanyRow, txtRegisterStatus);
        }

        private async Task LoadSubAreasAsync(CompanyRow? company, TextBlock status)
        {
            if (company == null)
            {
                cmbSubArea.ItemsSource = null;
                return;
            }

            try
            {
                var subs = await AdminDbService.GetSubAreasAsync(company.CompanyID);
                cmbSubArea.ItemsSource = subs;
                cmbSubArea.SelectedIndex = subs.Count > 0 ? 0 : -1;
            }
            catch (Exception ex)
            {
                Say(status, AdminDbService.FriendlyError(ex), false);
            }
        }

        private async void AddCompany_Click(object sender, RoutedEventArgs e)
        {
            if (!IsSuperAdmin) return;

            if (string.IsNullOrWhiteSpace(txtCompanyId.Text) || string.IsNullOrWhiteSpace(txtCompanyName.Text))
            {
                Say(txtOrgStatus, "Company ID and name are required.", false);
                return;
            }

            try
            {
                await AdminDbService.AddCompanyAsync(txtCompanyId.Text, txtCompanyName.Text);
                txtCompanyId.Clear();
                txtCompanyName.Clear();
                Say(txtOrgStatus, "Company added.", true);
                await ReloadCompaniesAsync();
            }
            catch (Exception ex)
            {
                Say(txtOrgStatus, AdminDbService.FriendlyError(ex), false);
            }
        }

        private async void AddSubArea_Click(object sender, RoutedEventArgs e)
        {
            if (!IsSuperAdmin) return;

            if (cmbMgrCompany.SelectedItem is not CompanyRow company)
            {
                Say(txtOrgStatus, "Select a company first.", false);
                return;
            }
            if (string.IsNullOrWhiteSpace(txtSubAreaId.Text) || string.IsNullOrWhiteSpace(txtSubAreaName.Text))
            {
                Say(txtOrgStatus, "Sub area ID and name are required.", false);
                return;
            }

            try
            {
                await AdminDbService.AddSubAreaAsync(company.CompanyID, txtSubAreaId.Text, txtSubAreaName.Text);
                txtSubAreaId.Clear();
                txtSubAreaName.Clear();
                Say(txtOrgStatus, "Sub area added.", true);
                await LoadSubAreasAsync(cmbCompany.SelectedItem as CompanyRow, txtRegisterStatus);
            }
            catch (Exception ex)
            {
                Say(txtOrgStatus, AdminDbService.FriendlyError(ex), false);
            }
        }

        // ------------------------------------------------------------
        // Agent (SuperAdmin only): register this computer and its printers
        // ------------------------------------------------------------
        private async void RegisterComputer_Click(object sender, RoutedEventArgs e)
        {
            if (!IsSuperAdmin) return;

            if (cmbCompany.SelectedItem is not CompanyRow company)
            {
                Say(txtRegisterStatus, "Select a company (add one in the Organization tab first).", false);
                return;
            }
            if (cmbSubArea.SelectedItem is not SubAreaRow sub)
            {
                Say(txtRegisterStatus, "Select a sub area (add one in the Organization tab first).", false);
                return;
            }

            var printers = lstAgentPrinters.SelectedItems.Cast<PrinterInfo>().ToList();
            if (printers.Count == 0)
            {
                Say(txtRegisterStatus, "Select at least one printer on this computer.", false);
                return;
            }

            string computerId = txtComputerId.Text;
            string agentName = string.IsNullOrWhiteSpace(txtAgentName.Text)
                ? Environment.MachineName
                : txtAgentName.Text.Trim();

            btnRegister.IsEnabled = false;
            Info(txtRegisterStatus, "Registering...");

            try
            {
                await AdminDbService.RegisterComputerAsync(computerId, agentName, company.CompanyID, sub.SubAreaID, printers);

                AppSettings.Update(c =>
                {
                    c.ComputerId = computerId;
                    c.AgentName = agentName;
                });

                Say(txtRegisterStatus, $"Computer registered with {printers.Count} printer(s).", true);
            }
            catch (Exception ex)
            {
                Say(txtRegisterStatus, AdminDbService.FriendlyError(ex), false);
            }
            finally
            {
                btnRegister.IsEnabled = true;
            }
        }

        // ------------------------------------------------------------
        // Job polling interval (local setting, every signed-in administrator)
        // ------------------------------------------------------------
        private void SavePoll_Click(object sender, RoutedEventArgs e)
        {
            if (!CanAdminister) return;

            if (!int.TryParse(txtPollSeconds.Text.Trim(), out int seconds) ||
                seconds < DbJobPoller.MinIntervalSeconds || seconds > DbJobPoller.MaxIntervalSeconds)
            {
                Say(txtPollStatus,
                    $"Enter a whole number between {DbJobPoller.MinIntervalSeconds} and {DbJobPoller.MaxIntervalSeconds}.",
                    false);
                return;
            }

            AppSettings.Update(c => c.PollIntervalSeconds = seconds);
            Say(txtPollStatus, $"Saved. This computer checks for new print jobs every {seconds} second(s).", true);
        }

        // ------------------------------------------------------------
        // Registered printers tab (view only, both admin roles)
        // ------------------------------------------------------------
        private async Task LoadRegisteredAsync()
        {
            try
            {
                var computers = await AdminDbService.GetComputersAsync();

                var items = new List<ComputerRow>
        {
            new ComputerRow { ComputerID = "", ComputerName = "All computers" }
        };
                items.AddRange(computers);

                string keep = (cmbRegComputer.SelectedItem as ComputerRow)?.ComputerID ?? "";
                cmbRegComputer.ItemsSource = items;
                cmbRegComputer.SelectedItem = items.FirstOrDefault(c => c.ComputerID == keep) ?? items[0];

                await LoadRegisteredPrintersAsync();
            }
            catch (Exception ex)
            {
                Say(txtRegisteredStatus, AdminDbService.FriendlyError(ex), false);
            }
        }

        private async Task LoadRegisteredPrintersAsync()
        {
            try
            {
                string? cid = (cmbRegComputer.SelectedItem as ComputerRow)?.ComputerID;
                var rows = await AdminDbService.GetRegisteredPrintersAsync(
                    string.IsNullOrEmpty(cid) ? null : cid);

                dgRegistered.ItemsSource = rows;
                Info(txtRegisteredStatus, $"{rows.Count} printer(s) shown.");
            }
            catch (Exception ex)
            {
                Say(txtRegisteredStatus, AdminDbService.FriendlyError(ex), false);
            }
        }

        private async void cmbRegComputer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoggedIn) return;
            await LoadRegisteredPrintersAsync();
        }

        private async void RefreshRegistered_Click(object sender, RoutedEventArgs e)
        {
            await LoadRegisteredAsync();
        }
    }
}