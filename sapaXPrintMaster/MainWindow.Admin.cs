using Microsoft.Data.SqlClient;
using sapaXPrintCore.Printers;
using sapaXPrintMaster.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace sapaXPrintMaster
{
    // Administrator tab. DELETE the old admin region from MainWindow.xaml.cs
    // (from "// ---- Admin / User login system" down to the end of Logout_Click).
    public partial class MainWindow
    {
        private bool _isAdminLoggedIn;
        private LoggedInUser? _currentUser;
        private string _apiKey = "";

        // ------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------
        private static void Say(TextBlock t, string msg, bool ok, bool show = true)
        {
            t.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            t.Foreground = ok ? Brushes.SeaGreen : Brushes.Firebrick;
            t.Text = (ok ? "✔ " : "✘ ") + msg;
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

        private void PopulateTableCombo()
        {
            cmbTemplateTable.ItemsSource = SchemaTemplates.Available(_isAdminLoggedIn).ToList();
            cmbTemplateTable.SelectedIndex = 0;
            btnCreateAll.Visibility = _isAdminLoggedIn ? Visibility.Visible : Visibility.Collapsed;
        }

        // ------------------------------------------------------------
        // State machine: decides which steps are visible
        // ------------------------------------------------------------
        private async void CheckFirstRunAsync() => await RefreshAdminStateAsync();

        private async Task RefreshAdminStateAsync()
        {
            LoadDbFields();

            pnlTablesStep.Visibility = pnlCreateAdmin.Visibility =
                pnlLogin.Visibility = pnlSignUp.Visibility = Visibility.Collapsed;

            if (_isAdminLoggedIn)
            {
                pnlAdmin.Visibility = Visibility.Visible;
                pnlTablesStep.Visibility = Visibility.Visible;
                return;
            }
            pnlAdmin.Visibility = Visibility.Collapsed;

            if (!DbConfig.IsConfigured)
            {
                txtDbStatus.Foreground = Brushes.Gray;
                txtDbStatus.Text = "Enter the database connection and click Test & Save.";
                return;
            }

            try
            {
                bool usersTable = await AdminDbService.TableExistsAsync("Users");
                txtDbStatus.Foreground = Brushes.SeaGreen;
                txtDbStatus.Text = "✔ Database connection saved.";

                PopulateTableCombo();
                pnlTablesStep.Visibility = Visibility.Visible;

                if (!usersTable)
                {
                    Say(txtTablesStatus, "Users table doesn't exist yet. Create it here first.", false);
                    return;
                }

                txtTablesStatus.Text = "";
                bool hasAdmin = await UsersService.SuperAdminExistsAsync();
                (hasAdmin ? pnlLogin : pnlCreateAdmin).Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Say(txtDbStatus, "Cannot reach the database: " + ex.Message, false);
            }
        }

        // ------------------------------------------------------------
        // Step 1: connection
        // ------------------------------------------------------------
        private void chkWindowsAuth_Changed(object sender, RoutedEventArgs e)
        {
            if (pnlSqlAuth == null) return;
            pnlSqlAuth.Visibility = chkWindowsAuth.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void SaveDb_Click(object sender, RoutedEventArgs e)
        {
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
            txtDbStatus.Foreground = Brushes.Gray;
            txtDbStatus.Text = "Testing connection...";

            try
            {
                var version = await AdminDbService.TestAsync(s);
                DbConfig.Save(s);                       // only saved after a successful test
                pwdDb.Clear();
                Say(txtDbStatus, $"Connected and saved (server {version}).", true);
                await RefreshAdminStateAsync();
            }
            catch (Exception ex)
            {
                Say(txtDbStatus, "Connection failed: " + ex.Message, false);
            }
            finally { btnSaveDb.IsEnabled = true; }
        }

        // ------------------------------------------------------------
        // Step 2: tables
        // ------------------------------------------------------------
        private async void CreateTable_Click(object sender, RoutedEventArgs e)
        {
            if (cmbTemplateTable.SelectedItem is not string name) return;
            btnCreateTable.IsEnabled = false;
            try
            {
                bool created = await AdminDbService.CreateTableAsync(name);
                Say(txtTablesStatus, created ? $"Table {name} created." : $"Table {name} already exists (columns checked / upgraded).", true);
                await RefreshAfterTableChange();
            }
            catch (Exception ex) { Say(txtTablesStatus, ex.Message, false); }
            finally { btnCreateTable.IsEnabled = true; }
        }

        private async void CreateAll_Click(object sender, RoutedEventArgs e)
        {
            if (!_isAdminLoggedIn) return;
            try
            {
                var created = await AdminDbService.CreateAllMissingAsync();
                Say(txtTablesStatus, created.Count == 0 ? "All tables already exist (columns checked / upgraded)."
                                                         : "Created: " + string.Join(", ", created), true);
                await RefreshAfterTableChange();
            }
            catch (Exception ex) { Say(txtTablesStatus, ex.Message, false); }
        }

        private async Task RefreshAfterTableChange()
        {
            if (_isAdminLoggedIn) await LoadTablesListAsync();
            else await RefreshAdminStateAsync();   // may reveal the "create admin" step
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
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error"); }
        }

        // ------------------------------------------------------------
        // Step 3: first admin / sign up
        // ------------------------------------------------------------
        private async void CreateFirstAdmin_Click(object sender, RoutedEventArgs e)
        {
            txtCreateAdminStatus.Visibility = Visibility.Visible;
            txtCreateAdminStatus.Foreground = Brushes.Gray;
            txtCreateAdminStatus.Text = "Creating account...";

            var (success, message) = await UsersService.CreateFirstAdminAsync(
                txtNewAdminUserId.Text, pwdNewAdminPassword.Password,
                txtNewAdminUserName.Text, txtNewAdminEmail.Text);

            Say(txtCreateAdminStatus, message, success);

            if (success)
            {
                pwdNewAdminPassword.Clear();
                await Task.Delay(800);
                pnlCreateAdmin.Visibility = Visibility.Collapsed;
                pnlLogin.Visibility = Visibility.Visible;
            }
        }

        private void ShowSignUp_Click(object sender, RoutedEventArgs e)
        {
            pnlLogin.Visibility = Visibility.Collapsed;
            pnlSignUp.Visibility = Visibility.Visible;
        }

        private void ShowLogin_Click(object sender, RoutedEventArgs e)
        {
            pnlSignUp.Visibility = Visibility.Collapsed;
            pnlLogin.Visibility = Visibility.Visible;
        }

        private async void SignUp_Click(object sender, RoutedEventArgs e)
        {
            txtSignupStatus.Visibility = Visibility.Visible;
            txtSignupStatus.Foreground = Brushes.Gray;
            txtSignupStatus.Text = "Creating account...";

            var (result, message) = await UsersService.SignUpAsync(
                txtSignupUserId.Text, pwdSignupPassword.Password, txtSignupUserName.Text,
                txtSignupEmail.Text, txtSignupActivationKey.Text);

            bool success = result == SignupResult.Success;
            Say(txtSignupStatus, message, success);

            if (success)
            {
                pwdSignupPassword.Clear();
                txtSignupActivationKey.Clear();
                await Task.Delay(1000);
                pnlSignUp.Visibility = Visibility.Collapsed;
                pnlLogin.Visibility = Visibility.Visible;
            }
        }

        // ------------------------------------------------------------
        // Step 4: login / logout
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

            if (result == LoginResult.Success && user != null)
            {
                _isAdminLoggedIn = true;
                _currentUser = user;
                txtLoggedInAs.Text = $"Logged in as: {user.UserId} ({user.Role})";

                pnlLogin.Visibility = Visibility.Collapsed;
                pnlAdmin.Visibility = Visibility.Visible;
                PopulateTableCombo();                 // now ALL templates
                await LoadAdminDataAsync();
            }
            else
            {
                txtLoginError.Text = message;
                txtLoginError.Visibility = Visibility.Visible;
            }
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            _isAdminLoggedIn = false;
            _currentUser = null;
            _apiKey = "";
            txtApiKey.Text = "";
            txtUsername.Clear();
            pwdPassword.Clear();
            PopulateTableCombo();                     // back to Users only
            _ = RefreshAdminStateAsync();
        }

        // ------------------------------------------------------------
        // After login: load everything
        // ------------------------------------------------------------
        private async Task LoadAdminDataAsync()
        {
            txtComputerId.Text = ComputerIdentityService.GetComputerId();

            var creds = AgentCredentialStore.Load();
            txtAgentName.Text = creds?.AgentName is { Length: > 0 } n ? n : Environment.MachineName;
            txtCloudUrl.Text = creds?.CloudUrl is { Length: > 0 } u ? u : AppSettings.CloudUrl;
            _apiKey = creds?.ComputerId == txtComputerId.Text ? creds?.ApiKey ?? "" : "";
            ShowKey_Changed(null!, null!);

            try
            {
                await LoadTablesListAsync();
                await ReloadCompaniesAsync();
            }
            catch (Exception ex)
            {
                Say(txtOrgStatus, "Could not load data (are all tables created?): " + ex.Message, false);
            }
        }

        // ------------------------------------------------------------
        // Companies / sub areas
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
            if (cmbCompany.SelectedItem is not CompanyRow c) { cmbSubArea.ItemsSource = null; return; }
            try
            {
                var subs = await AdminDbService.GetSubAreasAsync(c.CompanyID);
                cmbSubArea.ItemsSource = subs;
                cmbSubArea.SelectedIndex = subs.Count > 0 ? 0 : -1;
            }
            catch (Exception ex) { Say(txtRegisterStatus, ex.Message, false); }
        }

        private async void AddCompany_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtCompanyId.Text) || string.IsNullOrWhiteSpace(txtCompanyName.Text))
            { Say(txtOrgStatus, "Company ID and name are required.", false); return; }

            try
            {
                await AdminDbService.AddCompanyAsync(txtCompanyId.Text, txtCompanyName.Text);
                txtCompanyId.Clear(); txtCompanyName.Clear();
                Say(txtOrgStatus, "Company added.", true);
                await ReloadCompaniesAsync();
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601)
            { Say(txtOrgStatus, "That Company ID already exists.", false); }
            catch (Exception ex) { Say(txtOrgStatus, ex.Message, false); }
        }

        private async void AddSubArea_Click(object sender, RoutedEventArgs e)
        {
            if (cmbMgrCompany.SelectedItem is not CompanyRow c)
            { Say(txtOrgStatus, "Select a company first.", false); return; }
            if (string.IsNullOrWhiteSpace(txtSubAreaId.Text) || string.IsNullOrWhiteSpace(txtSubAreaName.Text))
            { Say(txtOrgStatus, "Sub area ID and name are required.", false); return; }

            try
            {
                await AdminDbService.AddSubAreaAsync(c.CompanyID, txtSubAreaId.Text, txtSubAreaName.Text);
                txtSubAreaId.Clear(); txtSubAreaName.Clear();
                Say(txtOrgStatus, "Sub area added.", true);
                cmbCompany_SelectionChanged(null!, null!);
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601)
            { Say(txtOrgStatus, "That Sub area ID already exists for this company.", false); }
            catch (Exception ex) { Say(txtOrgStatus, ex.Message, false); }
        }

        // ------------------------------------------------------------
        // Register computer + API key
        // ------------------------------------------------------------
        private async void RegisterComputer_Click(object sender, RoutedEventArgs e) => await RegisterAsync(false);

        private async void RegenerateKey_Click(object sender, RoutedEventArgs e)
        {
            var ok = MessageBox.Show(
                "Create a new API key? The old key stops working immediately and this agent must restart.",
                "Regenerate API key", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ok == MessageBoxResult.Yes) await RegisterAsync(true);
        }

        private async Task RegisterAsync(bool regenerate)
        {
            if (cmbCompany.SelectedItem is not CompanyRow company)
            { Say(txtRegisterStatus, "Select a company (add one above if the list is empty).", false); return; }
            if (cmbSubArea.SelectedItem is not SubAreaRow sub)
            { Say(txtRegisterStatus, "Select a sub area (add one above if the list is empty).", false); return; }

            var computerId = txtComputerId.Text;
            var agentName = string.IsNullOrWhiteSpace(txtAgentName.Text) ? Environment.MachineName : txtAgentName.Text.Trim();
            var cloudUrl = txtCloudUrl.Text.Trim();

            btnRegister.IsEnabled = false;
            txtRegisterStatus.Foreground = Brushes.Gray;
            txtRegisterStatus.Text = "Registering...";

            try
            {
                var rawKey = await AdminDbService.RegisterComputerAsync(
                    computerId, agentName, company.CompanyID, sub.SubAreaID, regenerate);

                var local = AgentCredentialStore.Load();
                var keepOld = rawKey == null && local?.ComputerId == computerId && !string.IsNullOrEmpty(local.ApiKey);

                if (rawKey == null && !keepOld)
                {
                    Say(txtRegisterStatus,
                        "This computer already has a key that was created elsewhere and can't be shown again. Click Regenerate.",
                        false);
                    return;
                }

                _apiKey = rawKey ?? local!.ApiKey;
                AgentCredentialStore.Save(new AgentCredentials
                {
                    ComputerId = computerId,
                    AgentName = agentName,
                    ApiKey = _apiKey,
                    CloudUrl = cloudUrl
                });
                ShowKey_Changed(null!, null!);

                Say(txtRegisterStatus,
                    rawKey != null ? "Registered. New API key created. Restart the app to start using it."
                                   : "Registration updated.", true);
                await LoadTablesListAsync();
            }
            catch (Exception ex) { Say(txtRegisterStatus, ex.Message, false); }
            finally { btnRegister.IsEnabled = true; }
        }

        private void ShowKey_Changed(object sender, RoutedEventArgs e)
        {
            if (txtApiKey == null) return;
            txtApiKey.Text = chkShowKey.IsChecked == true || string.IsNullOrEmpty(_apiKey)
                ? _apiKey
                : new string('•', 24);
        }

        private void CopyKey_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_apiKey)) Clipboard.SetText(_apiKey);
        }
    }
}