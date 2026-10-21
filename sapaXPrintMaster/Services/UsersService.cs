using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace sapaXPrintMaster.Services
{
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
        public const string SuperAdminRole = "SuperAdmin";
        public const string AdminRole = "Administrator";
        public const string UserRole = "User";

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
                "SELECT COUNT(1) FROM dbo.Users WHERE Role = @role", conn);
            cmd.Parameters.AddWithValue("@role", SuperAdminRole);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        // -------------------------------
        // First account: always a SuperAdmin. Only possible when none exists.
        // -------------------------------
        public static async Task<(bool Success, string Message)> CreateFirstAdminAsync(
            string userId, string password, string? userName, string? email)
        {
            if (await SuperAdminExistsAsync())
                return (false, "A SuperAdmin already exists.");

            return await InsertUserAsync(userId, password, SuperAdminRole, userName, email);
        }

        // -------------------------------
        // Add a user from the Administrator tab
        //   SuperAdmin    : can add Administrator or User
        //   Administrator : can add User only
        // -------------------------------
        public static async Task<(bool Success, string Message)> AddUserAsync(
            string creatorRole, string role, string userId, string password, string? userName, string? email)
        {
            if (creatorRole != SuperAdminRole && creatorRole != AdminRole)
                return (false, "Your account cannot add users.");

            if (role != AdminRole && role != UserRole)
                return (false, "Choose Administrator or User.");

            if (role == AdminRole && creatorRole != SuperAdminRole)
                return (false, "Only a SuperAdmin can add administrators.");

            return await InsertUserAsync(userId, password, role, userName, email);
        }

        private static async Task<(bool Success, string Message)> InsertUserAsync(
            string userId, string password, string role, string? userName, string? email)
        {
            userId = (userId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
                return (false, "User ID and password are required.");

            try
            {
                using var conn = new SqlConnection(DbConfig.ConnectionString);
                await conn.OpenAsync();

                using (var check = new SqlCommand("SELECT COUNT(1) FROM dbo.Users WHERE UserID = @id", conn))
                {
                    check.Parameters.AddWithValue("@id", userId);
                    if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0)
                        return (false, "That User ID is already taken.");
                }

                using var cmd = new SqlCommand(@"
                    INSERT INTO dbo.Users (UserID, PasswordHash, Role, UserName, Email, Active)
                    VALUES (@id, @hash, @role, @name, @email, 1)", conn);

                cmd.Parameters.AddWithValue("@id", userId);
                cmd.Parameters.Add("@hash", SqlDbType.VarBinary, 32).Value = HashPassword(password);
                cmd.Parameters.AddWithValue("@role", role);
                cmd.Parameters.AddWithValue("@name", string.IsNullOrWhiteSpace(userName) ? DBNull.Value : userName.Trim());
                cmd.Parameters.AddWithValue("@email", string.IsNullOrWhiteSpace(email) ? DBNull.Value : email.Trim());

                await cmd.ExecuteNonQueryAsync();
                return (true, $"{role} '{userId}' created.");
            }
            catch (Exception ex)
            {
                return (false, AdminDbService.FriendlyError(ex));
            }
        }

        // -------------------------------
        // Sign in
        // -------------------------------
        public static async Task<(LoginResult Result, LoggedInUser? User, string Message)> LoginAsync(
            string userId, string password)
        {
            userId = (userId ?? "").Trim();

            try
            {
                using var conn = new SqlConnection(DbConfig.ConnectionString);
                await conn.OpenAsync();

                using var cmd = new SqlCommand(
                    "SELECT PasswordHash, Role, UserName, Active FROM dbo.Users WHERE UserID = @id", conn);
                cmd.Parameters.AddWithValue("@id", userId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return (LoginResult.NotFound, null, "Invalid User ID or password.");

                var storedHash = (byte[])reader["PasswordHash"];
                string role = reader.GetString(1);
                string? userName = reader.IsDBNull(2) ? null : reader.GetString(2);
                bool active = reader.GetBoolean(3);

                if (!storedHash.AsSpan().SequenceEqual(HashPassword(password)))
                    return (LoginResult.WrongPassword, null, "Invalid User ID or password.");

                if (!active)
                    return (LoginResult.AccountInactive, null, "This account is not active.");

                return (LoginResult.Success,
                    new LoggedInUser { UserId = userId, Role = role, UserName = userName },
                    "Signed in.");
            }
            catch (Exception ex)
            {
                return (LoginResult.Error, null, AdminDbService.FriendlyError(ex));
            }
        }
    }
}