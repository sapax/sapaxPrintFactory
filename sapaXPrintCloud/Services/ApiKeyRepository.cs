using sapaXPrintCloud.Models;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace sapaXPrintCloud.Services
{
    public static class ApiKeyRepository
    {
        private static string _connectionString = "";

        // REQUIRED INIT (Program.cs)
        public static void Init(IConfiguration config)
        {
            _connectionString =
                config.GetConnectionString("DefaultConnection")
                ?? throw new Exception("DB connection string missing");

            // 🔎 Fail fast if DB unreachable (prevents Azure 500)
            try
            {
                using var con = new SqlConnection(_connectionString);
                con.Open();
            }
            catch (Exception ex)
            {
                Console.WriteLine("DB Syncing issue/ DB not live.");
                Console.WriteLine(ex.Message);
            }
        }

        public static bool Validate(string rawKey)
        {
            var hash = Hash(rawKey);

            using var con = new SqlConnection(_connectionString);
            con.Open();

            using var cmd = new SqlCommand(
                @"SELECT COUNT(1)
                  FROM ApiKeys
                  WHERE KeyHash = @hash
                  AND IsActive = 1",
                con);

            cmd.Parameters.Add("@hash", System.Data.SqlDbType.VarBinary, 32)
               .Value = hash;

            return (int)cmd.ExecuteScalar()! > 0;
        }

        public static void Save(ApiKey key)
        {
            using var con = new SqlConnection(_connectionString);
            con.Open();

            using var cmd = new SqlCommand(@"
                INSERT INTO ApiKeys
                (ApiKeyId, KeyPrefix, KeyHash, OwnerType, OwnerId, IsActive, CreatedAt)
                VALUES
                (@id, @prefix, @hash, @type, @owner, @active, @created)",
                con);

            cmd.Parameters.AddWithValue("@id", key.ApiKeyId);
            cmd.Parameters.AddWithValue("@prefix", key.KeyPrefix);

            cmd.Parameters.Add("@hash", System.Data.SqlDbType.VarBinary, 32)
               .Value = key.KeyHash;

            cmd.Parameters.AddWithValue("@type", key.OwnerType);
            cmd.Parameters.AddWithValue("@owner", key.OwnerId);
            cmd.Parameters.AddWithValue("@active", key.IsActive);
            cmd.Parameters.AddWithValue("@created", key.CreatedAt);

            cmd.ExecuteNonQuery();
        }

        private static byte[] Hash(string input)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        }
    }
}
