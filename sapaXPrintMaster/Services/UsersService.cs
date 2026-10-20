using Microsoft.Data.SqlClient;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace sapaXPrintMaster.Services
{
    public enum SignupResult
    {
        Success,
        UserIdTaken,
        InvalidActivationKey,
        Error
    }

    public enum LoginResult
    {
        Success,
        NotFound,
        WrongPassword,
        AccountInactive,
        Error
    }

    public class LoggedInUser
    {
        public string UserId { get; set; } = "";
        public string Role { get; set; } = "";
        public string? UserName { get; set; }
    }

    public static class UsersService
    {
  

        private const string SuperAdminPrefix = "sapax_";

        private static byte[] HashPassword(string password) =>
            SHA256.HashData(Encoding.UTF8.GetBytes(password));

        // -------------------------------
        // First-run check
        // -------------------------------
        public static async Task<bool> SuperAdminExistsAsync()
        {
            using var conn = new SqlConnection(DbConfig.ConnectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                "SELECT COUNT(1) FROM [dbo].[Users] WHERE Role = 'SuperAdmin'", conn);

            return (int)await cmd.ExecuteScalarAsync() > 0;
        }

        // -------------------------------
        // Create the first Administrator account
        // (dedicated first-run form — prefix is implicit, not typed by the user)
        // -------------------------------
        public static async Task<(bool Success, string Message)> CreateFirstAdminAsync(
            string userId, string password, string? userName, string? email)
        {
            userId = userId.Trim();

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
                return (false, "User ID and password are required.");

            try
            {
                using var conn = new SqlConnection(DbConfig.ConnectionString);
                await conn.OpenAsync();

                using var check = new SqlCommand(
                    "SELECT COUNT(1) FROM [dbo].[Users] WHERE UserID = @id", conn);
                check.Parameters.AddWithValue("@id", userId);
                if ((int)await check.ExecuteScalarAsync() > 0)
                    return (false, "That User ID is already taken.");

                using var cmd = new SqlCommand(@"
                    INSERT INTO [dbo].[Users] (UserID, PasswordHash, Role, UserName, Email, Active)
                    VALUES (@id, @hash, 'SuperAdmin', @name, @email, 1)", conn);

                cmd.Parameters.AddWithValue("@id", userId);
                cmd.Parameters.Add("@hash", System.Data.SqlDbType.VarBinary, 32).Value = HashPassword(password);
                cmd.Parameters.AddWithValue("@name", string.IsNullOrWhiteSpace(userName) ? DBNull.Value : userName.Trim());
                cmd.Parameters.AddWithValue("@email", string.IsNullOrWhiteSpace(email) ? DBNull.Value : email.Trim());

                await cmd.ExecuteNonQueryAsync();
                return (true, "Administrator account created.");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        // -------------------------------
        // Sign up (after a SuperAdmin already exists)
        // "sapax_{id}" -> SuperAdmin, Active immediately
        // plain "{id}" -> User, requires a valid activation key
        // -------------------------------
        public static async Task<(SignupResult Result, string Message)> SignUpAsync(
            string rawUserId, string password, string? userName, string? email, string? activationKey)
        {
            rawUserId = rawUserId.Trim();

            if (string.IsNullOrWhiteSpace(rawUserId) || string.IsNullOrWhiteSpace(password))
                return (SignupResult.Error, "User ID and password are required.");

            bool isSuperAdmin = rawUserId.StartsWith(SuperAdminPrefix, StringComparison.OrdinalIgnoreCase);
            string userId = isSuperAdmin ? rawUserId[SuperAdminPrefix.Length..] : rawUserId;

            if (string.IsNullOrWhiteSpace(userId))
                return (SignupResult.Error, "User ID cannot be empty.");

            try
            {
                using var conn = new SqlConnection(DbConfig.ConnectionString);
                await conn.OpenAsync();

                using (var check = new SqlCommand(
                    "SELECT COUNT(1) FROM [dbo].[Users] WHERE UserID = @id", conn))
                {
                    check.Parameters.AddWithValue("@id", userId);
                    if ((int)await check.ExecuteScalarAsync() > 0)
                        return (SignupResult.UserIdTaken, "That User ID is already taken.");
                }

                bool active;

                if (isSuperAdmin)
                {
                    active = true; // SuperAdmins don't need an activation key
                }
                else
                {
                    // Normal user — must supply a currently valid activation key
                    bool keyValid = await IsActivationKeyValidAsync(conn, activationKey);
                    if (!keyValid)
                        return (SignupResult.InvalidActivationKey,
                            "Invalid or expired activation key. Account was not created.");
                    active = true;
                }

                using var cmd = new SqlCommand(@"
                    INSERT INTO [dbo].[Users] (UserID, PasswordHash, Role, UserName, Email, Active)
                    VALUES (@id, @hash, @role, @name, @email, @active)", conn);

                cmd.Parameters.AddWithValue("@id", userId);
                cmd.Parameters.Add("@hash", System.Data.SqlDbType.VarBinary, 32).Value = HashPassword(password);
                cmd.Parameters.AddWithValue("@role", isSuperAdmin ? "SuperAdmin" : "User");
                cmd.Parameters.AddWithValue("@name", string.IsNullOrWhiteSpace(userName) ? DBNull.Value : userName.Trim());
                cmd.Parameters.AddWithValue("@email", string.IsNullOrWhiteSpace(email) ? DBNull.Value : email.Trim());
                cmd.Parameters.AddWithValue("@active", active);

                await cmd.ExecuteNonQueryAsync();
                return (SignupResult.Success, "Account created successfully. You can now log in.");
            }
            catch (Exception ex)
            {
                return (SignupResult.Error, $"Error: {ex.Message}");
            }
        }

        private static async Task<bool> IsActivationKeyValidAsync(SqlConnection conn, string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;

            using var cmd = new SqlCommand(
                "SELECT Active, Expiration, ExpirationDate FROM [dbo].[ActivationKeys] WHERE ActivationKey = @key",
                conn);
            cmd.Parameters.AddWithValue("@key", key.Trim());

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return false;

            bool active = reader.GetBoolean(0);
            bool expiration = reader.GetBoolean(1);
            DateTime? expirationDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2);

            if (!active) return false;
            if (expiration && expirationDate.HasValue && expirationDate.Value <= DateTime.UtcNow) return false;

            return true;
        }

        // -------------------------------
        // Login — always UserID without prefix
        // -------------------------------
        public static async Task<(LoginResult Result, LoggedInUser? User, string Message)> LoginAsync(
            string userId, string password)
        {
            userId = userId.Trim();

            try
            {
                using var conn = new SqlConnection(DbConfig.ConnectionString);
                await conn.OpenAsync();

                using var cmd = new SqlCommand(
                    "SELECT PasswordHash, Role, UserName, Active FROM [dbo].[Users] WHERE UserID = @id", conn);
                cmd.Parameters.AddWithValue("@id", userId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return (LoginResult.NotFound, null, "Invalid User ID or password.");

                var storedHash = (byte[])reader["PasswordHash"];
                string role = reader.GetString(1);
                string? userName = reader.IsDBNull(2) ? null : reader.GetString(2);
                bool active = reader.GetBoolean(3);

                var enteredHash = HashPassword(password);
                if (!storedHash.AsSpan().SequenceEqual(enteredHash))
                    return (LoginResult.WrongPassword, null, "Invalid User ID or password.");

                if (!active)
                    return (LoginResult.AccountInactive, null, "This account is not active.");

                return (LoginResult.Success, new LoggedInUser { UserId = userId, Role = role, UserName = userName },
                    "Login successful.");
            }
            catch (Exception ex)
            {
                return (LoginResult.Error, null, $"Error: {ex.Message}");
            }
        }
    }
}