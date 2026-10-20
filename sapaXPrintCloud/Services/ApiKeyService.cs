using sapaXPrintCloud.Models;
using System.Security.Cryptography;
using System.Text;

namespace sapaXPrintCloud.Services
{
    public static class ApiKeyService
    {
        public static string Generate(string ownerType)
        {
            // RAW key (returned once)
            var raw =
                $"{ownerType}_{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";

            var apiKey = new ApiKey
            {
                ApiKeyId = Guid.NewGuid(),
                KeyPrefix = raw[..16],
                KeyHash = Hash(raw),          //  byte[]
                OwnerType = ownerType,
                OwnerId = "system",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            ApiKeyRepository.Save(apiKey);

            return raw; // raw key returned ONLY ONCE
        }

        private static byte[] Hash(string input)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        }
    }
}
