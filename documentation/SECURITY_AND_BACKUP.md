# Security and Backup Operations

## Encryption at Rest
- Documents: enable `Security:DocumentEncryptionEnabled` and set `Security:DocumentEncryptionKey` (base64, 32 bytes). The system stores encrypted files on disk and decrypts on download or indexing.
- Database: enable `Security:DbEncryptionEnabled` and set `Security:DbEncryptionKey` (base64, 32 bytes). Selected columns (PII and message bodies) are encrypted at the application layer.
- Tokenized document search still works; phrase matching is evaluated in memory for candidates.

### Key Generation (example)
```
openssl rand -base64 32
```

## Audit Log Immutability
- Set `Security:AuditLogImmutable` to `true` and provide `Security:AuditLogKey` (base64, 32 bytes).
- Each log entry stores a hash chain (sequence, previous hash, hash, algorithm).
- Verify integrity via `GET /api/admin/audit-logs/integrity`.

## Backup and Restore
### Create Backup
`POST /api/admin/backups`
```json
{ "includeUploads": true }
```

### Download Backup
`GET /api/admin/backups/{fileName}`

### Restore (Dry Run First)
`POST /api/admin/backups/restore`
```json
{ "fileName": "jurisflow-backup-YYYYMMDD-HHmmss.zip.enc", "includeUploads": true, "dryRun": true }
```

### Restore (Apply)
- Set `Backup:AllowRestore` to `true`.
- Recommended: stop the application or enter maintenance mode to avoid DB locks.
```json
{ "fileName": "jurisflow-backup-YYYYMMDD-HHmmss.zip.enc", "includeUploads": true, "dryRun": false }
```

### Backup Encryption
- Enable `Backup:EncryptBackups` and set `Backup:EncryptionKey` (base64, 32 bytes).
- Encrypted backups are saved as `.zip.enc`.

## Legal Hold Lock
- Documents on legal hold cannot be deleted, versioned, or updated until released.
