using System.Text;
using Shared.Models;

namespace Shared.Engine
{
    public static class StorageManager
    {
        private const string TempDirectoryName = "temp";

        private static readonly string BaseStoragePath =
            Path.GetFullPath("database/storage");

        private static readonly string BaseTempStoragePath = $"{BaseStoragePath}/{TempDirectoryName}";

        static StorageManager()
        {
            Directory.CreateDirectory(BaseStoragePath);
            Directory.CreateDirectory(BaseTempStoragePath);
        }

        public static string? GetFilePath(
            string pathType,
            bool createDirectory = false,
            RequestModel? requestInfo = null,
            string? profileUid = null)
        {
            return GetFilePath(pathType, createDirectory, requestInfo?.user_uid, profileUid);
        }

        public static string? GetFilePath(
            string pathType,
            bool createDirectory = false,
            string? userUid = null,
            string? profileUid = null)
        {
            if (string.IsNullOrWhiteSpace(userUid))
            {
                return null;
            }

            string cleanedUserId = CleanIdentifier(userUid);

            if (string.IsNullOrEmpty(cleanedUserId))
            {
                return null;
            }

            string cleanedProfileUid = CleanIdentifier(profileUid ?? string.Empty);
            string hashInput = cleanedUserId + cleanedProfileUid;
            string md5Key = CrypTo.md5(hashInput);

            string safePathType = CleanPathType(pathType);
            if (string.IsNullOrEmpty(safePathType))
            {
                return null;
            }

            string fullPath;
            try
            {
                if (safePathType == TempDirectoryName)
                {
                    fullPath = $"{BaseStoragePath}/{TempDirectoryName}/{md5Key}";
                }
                else
                {
                    string level1 = md5Key[..2];
                    string level2 = md5Key[2..];

                    string directoryPath = $"{BaseStoragePath}/{safePathType}/{level1}";
                    fullPath = $"{directoryPath}/{level2}";
                }
            }
            catch
            {
                return null;
            }

            if (!fullPath.StartsWith(BaseStoragePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullPath, BaseStoragePath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!createDirectory || safePathType == TempDirectoryName)
            {
                return fullPath;
            }

            try
            {
                string directoryPath = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
            }
            catch
            {
                // ignore all exceptions
                return null;
            }

            return fullPath;
        }

        private static string CleanIdentifier(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            var sb = new StringBuilder(input.Length);

            foreach (var c in input.Where(c => char.IsLetterOrDigit(c)
                || c is '-' or '_' or '@' or '.' or '+' or '='))
            {
                sb.Append(c);
            }

            string cleaned = sb.ToString();

            if (cleaned.Length is 0 or > 160 
                || cleaned.StartsWith(".") 
                || cleaned.EndsWith(".") 
                || cleaned.Contains("..") 
                || cleaned.Contains("//") 
                || cleaned.Contains(@"\") 
                || cleaned.Contains(":"))
            {
                return "";
            }

            return cleaned;
        }
        private static string CleanPathType(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            var sb = new StringBuilder();

            foreach (var c in input.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c)
                || c is '-' or '_'))
            {
                sb.Append(c);
            }

            string cleaned = sb.ToString();

            if (cleaned.Length is 0 or > 40
                || cleaned.Contains("..")
                || cleaned.StartsWith("-")
                || cleaned.EndsWith("-"))
            {
                return "";
            }

            return cleaned;
        }
    }
}