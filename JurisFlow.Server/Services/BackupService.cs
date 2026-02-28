using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;

namespace JurisFlow.Server.Services
{
    public record BackupFileInfo(string FileName, long SizeBytes, DateTime CreatedAt, bool IsEncrypted);

    public record BackupCreateResult(
        string FileName,
        long SizeBytes,
        bool IsEncrypted,
        DateTime CreatedAt,
        long DatabaseSizeBytes,
        int UploadFileCount,
        long UploadBytes);

    public record BackupRestoreResult(
        bool Success,
        bool DryRun,
        string Message,
        long DatabaseSizeBytes,
        int UploadFileCount,
        long UploadBytes);

    public class BackupService
    {
        private const int KeySizeBytes = 32;
        private const int IvSizeBytes = 12;
        private const int TagSizeBytes = 16;
        private static readonly Regex SafeBackupFileNamePattern = new("^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);
        private static readonly HashSet<string> AllowedBackupExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",
            ".enc"
        };
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;
        private readonly JurisFlowDbContext _context;
        private readonly ILogger<BackupService> _logger;
        private readonly TenantContext _tenantContext;
        private readonly byte[]? _backupKey;

        public BackupService(
            IConfiguration configuration,
            IWebHostEnvironment env,
            JurisFlowDbContext context,
            ILogger<BackupService> logger,
            TenantContext tenantContext)
        {
            _configuration = configuration;
            _env = env;
            _context = context;
            _logger = logger;
            _tenantContext = tenantContext;

            var rawKey = _configuration["Backup:EncryptionKey"];
            if (!string.IsNullOrWhiteSpace(rawKey))
            {
                try
                {
                    _backupKey = Convert.FromBase64String(rawKey);
                }
                catch (FormatException ex)
                {
                    _logger.LogError(ex, "Backup encryption key is not valid base64.");
                    _backupKey = null;
                }
            }
        }

        public IEnumerable<BackupFileInfo> ListBackups()
        {
            EnsureTenantContext();
            var backupRoot = GetBackupRoot();
            if (!Directory.Exists(backupRoot))
            {
                return Array.Empty<BackupFileInfo>();
            }

            return new DirectoryInfo(backupRoot)
                .GetFiles()
                .Where(f => AllowedBackupExtensions.Contains(f.Extension))
                .OrderByDescending(f => f.CreationTimeUtc)
                .Select(f => new BackupFileInfo(
                    f.Name,
                    f.Length,
                    f.CreationTimeUtc,
                    f.Extension.Equals(".enc", StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        public async Task<BackupCreateResult> CreateBackupAsync(bool includeUploads)
        {
            EnsureTenantContext();
            await EnsureSingleTenantAsync();
            var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("Tenant is required for backups.");

            var encryptBackups = _configuration.GetValue("Backup:EncryptBackups", true);
            if (encryptBackups && (_backupKey == null || _backupKey.Length != KeySizeBytes))
            {
                throw new InvalidOperationException("Backup encryption is enabled but the encryption key is missing or invalid.");
            }

            var backupRoot = GetBackupRoot();
            Directory.CreateDirectory(backupRoot);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var zipFileName = $"jurisflow-backup-{timestamp}.zip";
            var zipPath = Path.Combine(backupRoot, zipFileName);
            var encryptedPath = Path.Combine(backupRoot, zipFileName + ".enc");

            var dbPath = ResolveDatabasePath();
            if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
            {
                throw new InvalidOperationException("Database file not found for backup.");
            }

            var dbSnapshotPath = await CreateDatabaseSnapshotAsync(dbPath, backupRoot);
            var databaseSizeBytes = new FileInfo(dbSnapshotPath).Length;

            var uploadsRoot = Path.Combine(_env.ContentRootPath, "uploads", tenantId);
            var uploadFiles = includeUploads && Directory.Exists(uploadsRoot)
                ? Directory.GetFiles(uploadsRoot, "*", SearchOption.AllDirectories)
                : Array.Empty<string>();

            var uploadBytes = uploadFiles.Sum(f => new FileInfo(f).Length);
            try
            {
                using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    archive.CreateEntryFromFile(dbSnapshotPath, "database/jurisflow.db");

                    if (includeUploads && Directory.Exists(uploadsRoot))
                    {
                        foreach (var file in uploadFiles)
                        {
                            var relative = Path.GetRelativePath(uploadsRoot, file);
                            archive.CreateEntryFromFile(file, Path.Combine("uploads", relative));
                        }
                    }

                    var manifest = new
                    {
                        backupId = Guid.NewGuid().ToString(),
                        createdAt = DateTime.UtcNow,
                        databaseFile = "database/jurisflow.db",
                        databaseSizeBytes,
                        uploadsIncluded = includeUploads,
                        uploadFileCount = uploadFiles.Length,
                        uploadBytes,
                        documentEncryptionEnabled = _configuration.GetValue("Security:DocumentEncryptionEnabled", false),
                        dbEncryptionEnabled = _configuration.GetValue("Security:DbEncryptionEnabled", false)
                    };

                    var manifestEntry = archive.CreateEntry("manifest.json");
                    await using var entryStream = manifestEntry.Open();
                    await JsonSerializer.SerializeAsync(entryStream, manifest, new JsonSerializerOptions { WriteIndented = true });
                }

                if (encryptBackups)
                {
                    await EncryptBackupAsync(zipPath, encryptedPath);
                    File.Delete(zipPath);
                    var encryptedSize = new FileInfo(encryptedPath).Length;

                    return new BackupCreateResult(
                        Path.GetFileName(encryptedPath),
                        encryptedSize,
                        true,
                        DateTime.UtcNow,
                        databaseSizeBytes,
                        uploadFiles.Length,
                        uploadBytes);
                }

                var size = new FileInfo(zipPath).Length;
                return new BackupCreateResult(
                    zipFileName,
                    size,
                    false,
                    DateTime.UtcNow,
                    databaseSizeBytes,
                    uploadFiles.Length,
                    uploadBytes);
            }
            catch
            {
                if (encryptBackups)
                {
                    // Prevent plaintext artifacts when encrypted backup creation fails.
                    TryDeleteFile(zipPath);
                    TryDeleteFile(encryptedPath);
                }
                throw;
            }
            finally
            {
                TryDeleteFile(dbSnapshotPath);
            }
        }

        public async Task<BackupRestoreResult> RestoreBackupAsync(string fileName, bool includeUploads, bool dryRun)
        {
            EnsureTenantContext();
            await EnsureSingleTenantAsync();
            var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("Tenant is required for backups.");

            var backupRoot = GetBackupRoot();
            var safeName = ValidateBackupFileName(fileName);
            var backupPath = ResolveBackupPath(safeName);
            if (!File.Exists(backupPath))
            {
                throw new InvalidOperationException("Backup file is not available.");
            }

            var allowRestore = _configuration.GetValue("Backup:AllowRestore", false);
            if (!allowRestore && !dryRun)
            {
                return new BackupRestoreResult(false, dryRun, "Restore is disabled by configuration.", 0, 0, 0);
            }

            var tempRoot = Path.Combine(backupRoot, "_restore_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            var zipPath = backupPath;
            if (backupPath.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
            {
                zipPath = Path.Combine(tempRoot, "backup.zip");
                await DecryptBackupAsync(backupPath, zipPath);
            }

            ExtractZipSafely(zipPath, tempRoot);

            var dbFile = Path.Combine(tempRoot, "database", "jurisflow.db");
            if (!File.Exists(dbFile))
            {
                return new BackupRestoreResult(false, dryRun, "Database file not found in backup.", 0, 0, 0);
            }

            var uploadsRoot = Path.Combine(tempRoot, "uploads");
            var uploadFiles = includeUploads && Directory.Exists(uploadsRoot)
                ? Directory.GetFiles(uploadsRoot, "*", SearchOption.AllDirectories)
                : Array.Empty<string>();
            var uploadBytes = uploadFiles.Sum(f => new FileInfo(f).Length);
            var dbSize = new FileInfo(dbFile).Length;

            if (dryRun)
            {
                CleanupTemp(tempRoot);
                return new BackupRestoreResult(true, dryRun, "Dry run completed.", dbSize, uploadFiles.Length, uploadBytes);
            }

            var targetDbPath = ResolveDatabasePath();
            if (string.IsNullOrWhiteSpace(targetDbPath))
            {
                CleanupTemp(tempRoot);
                return new BackupRestoreResult(false, dryRun, "Database path could not be resolved.", dbSize, uploadFiles.Length, uploadBytes);
            }

            await _context.Database.CloseConnectionAsync();
            SqliteConnection.ClearAllPools();

            var targetDbDirectory = Path.GetDirectoryName(targetDbPath);
            if (!string.IsNullOrWhiteSpace(targetDbDirectory))
            {
                Directory.CreateDirectory(targetDbDirectory);
            }
            File.Copy(dbFile, targetDbPath, true);

            if (includeUploads)
            {
                var targetUploads = Path.Combine(_env.ContentRootPath, "uploads", tenantId);
                Directory.CreateDirectory(targetUploads);
                CopyDirectory(uploadsRoot, targetUploads);
            }

            CleanupTemp(tempRoot);
            return new BackupRestoreResult(true, dryRun, "Restore completed.", dbSize, uploadFiles.Length, uploadBytes);
        }

        private string GetBackupRoot()
        {
            var root = Path.Combine(_env.ContentRootPath, "backups");
            if (!string.IsNullOrWhiteSpace(_tenantContext.TenantSlug))
            {
                return Path.Combine(root, _tenantContext.TenantSlug);
            }
            if (!string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                return Path.Combine(root, _tenantContext.TenantId);
            }
            return root;
        }

        public string ResolveBackupPath(string fileName)
        {
            EnsureTenantContext();
            var safeName = ValidateBackupFileName(fileName);
            var backupRoot = Path.GetFullPath(GetBackupRoot());
            var candidatePath = Path.GetFullPath(Path.Combine(backupRoot, safeName));
            var candidateDirectory = Path.GetDirectoryName(candidatePath);
            if (!string.Equals(candidateDirectory, backupRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid backup file path.");
            }

            return candidatePath;
        }

        private static string ValidateBackupFileName(string fileName)
        {
            var safeName = Path.GetFileName(fileName);
            if (!string.Equals(fileName, safeName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Invalid backup file name.");
            }

            if (!SafeBackupFileNamePattern.IsMatch(safeName))
            {
                throw new InvalidOperationException("Invalid backup file name.");
            }

            var extension = Path.GetExtension(safeName);
            if (string.IsNullOrWhiteSpace(extension) || !AllowedBackupExtensions.Contains(extension))
            {
                throw new InvalidOperationException("Unsupported backup file type.");
            }

            return safeName;
        }

        private void EnsureTenantContext()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant is required for backups.");
            }
        }

        private async Task EnsureSingleTenantAsync()
        {
            var tenantCount = await _context.Tenants.CountAsync();
            if (tenantCount > 1)
            {
                throw new InvalidOperationException("Backups are disabled for shared databases. Use per-tenant databases or export tooling.");
            }
        }

        private string? ResolveDatabasePath()
        {
            var connectionCandidates = new[]
            {
                _context.Database.GetDbConnection().ConnectionString,
                _context.Database.GetConnectionString(),
                _configuration.GetConnectionString("DefaultConnection")
            };

            foreach (var candidate in connectionCandidates.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                var resolved = TryResolveSqlitePath(candidate!);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            return null;
        }

        private string? TryResolveSqlitePath(string connectionString)
        {
            try
            {
                var builder = new SqliteConnectionStringBuilder(connectionString);
                var dataSource = builder.DataSource;
                if (string.IsNullOrWhiteSpace(dataSource) ||
                    string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return Path.IsPathRooted(dataSource)
                    ? dataSource
                    : Path.GetFullPath(Path.Combine(_env.ContentRootPath, dataSource));
            }
            catch
            {
                return null;
            }
        }

        private async Task EncryptBackupAsync(string inputPath, string outputPath)
        {
            var key = RequireBackupKey();
            var plaintext = await File.ReadAllBytesAsync(inputPath);
            var iv = RandomNumberGenerator.GetBytes(IvSizeBytes);
            var tag = new byte[TagSizeBytes];
            var ciphertext = new byte[plaintext.Length];

            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Encrypt(iv, plaintext, ciphertext, tag);

            var envelope = new byte[iv.Length + tag.Length + ciphertext.Length];
            Buffer.BlockCopy(iv, 0, envelope, 0, iv.Length);
            Buffer.BlockCopy(tag, 0, envelope, iv.Length, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, envelope, iv.Length + tag.Length, ciphertext.Length);

            await File.WriteAllBytesAsync(outputPath, envelope);
        }

        private async Task DecryptBackupAsync(string inputPath, string outputPath)
        {
            var key = RequireBackupKey();
            var envelope = await File.ReadAllBytesAsync(inputPath);
            if (envelope.Length <= IvSizeBytes + TagSizeBytes)
            {
                throw new InvalidOperationException("Encrypted backup file is invalid.");
            }

            var iv = new byte[IvSizeBytes];
            var tag = new byte[TagSizeBytes];
            var ciphertext = new byte[envelope.Length - IvSizeBytes - TagSizeBytes];

            Buffer.BlockCopy(envelope, 0, iv, 0, IvSizeBytes);
            Buffer.BlockCopy(envelope, IvSizeBytes, tag, 0, TagSizeBytes);
            Buffer.BlockCopy(envelope, IvSizeBytes + TagSizeBytes, ciphertext, 0, ciphertext.Length);

            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(iv, ciphertext, tag, plaintext);

            await File.WriteAllBytesAsync(outputPath, plaintext);
        }

        private byte[] RequireBackupKey()
        {
            if (_backupKey == null || _backupKey.Length != KeySizeBytes)
            {
                throw new InvalidOperationException("Backup encryption key is missing or invalid.");
            }
            return _backupKey;
        }

        private static void CopyDirectory(string source, string destination)
        {
            if (!Directory.Exists(source))
            {
                return;
            }

            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, dir);
                Directory.CreateDirectory(Path.Combine(destination, relative));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
        }

        private static void ExtractZipSafely(string zipPath, string destinationRoot)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var fullRoot = Path.GetFullPath(destinationRoot);
            var fullRootWithSeparator = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            foreach (var entry in archive.Entries)
            {
                var normalizedEntryPath = entry.FullName
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);
                var destinationPath = Path.GetFullPath(Path.Combine(fullRoot, normalizedEntryPath));

                if (!destinationPath.StartsWith(fullRootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(destinationPath, fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Backup archive contains invalid paths.");
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                entry.ExtractToFile(destinationPath, true);
            }
        }

        private static void CleanupTemp(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        private async Task<string> CreateDatabaseSnapshotAsync(string sourceDbPath, string backupRoot)
        {
            await _context.Database.CloseConnectionAsync();
            SqliteConnection.ClearAllPools();

            var snapshotPath = Path.Combine(backupRoot, $"_dbsnapshot_{Guid.NewGuid():N}.db");

            await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = sourceDbPath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString()))
            await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = snapshotPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString()))
            {
                await source.OpenAsync();
                await destination.OpenAsync();
                source.BackupDatabase(destination);

                await destination.CloseAsync();
                await source.CloseAsync();
            }

            SqliteConnection.ClearAllPools();

            return snapshotPath;
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
