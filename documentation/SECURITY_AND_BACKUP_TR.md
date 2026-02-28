# Guvenlik ve Yedekleme Islemleri

## Veri Saklama Sirasinda Sifreleme (Encryption at Rest)
- Dokumanlar: `Security:DocumentEncryptionEnabled` etkinlestirin ve `Security:DocumentEncryptionKey` (base64, 32 byte) ayarlayin. Sistem dosyalari disk uzerinde sifreler, indirme veya indeksleme sirasinda cozer.
- Veritabani: `Security:DbEncryptionEnabled` etkinlestirin ve `Security:DbEncryptionKey` (base64, 32 byte) ayarlayin. Secili kolonlar (PII ve mesaj govdeleri) uygulama katmaninda sifrelenir.
- Token tabanli dokuman aramasi calismaya devam eder; phrase eslesmesi adaylar icin bellekte degerlendirilir.

### Anahtar Uretimi (ornek)
```
openssl rand -base64 32
```

## Denetim Kaydi Degistirilemezligi
- `Security:AuditLogImmutable` degerini `true` yapin ve `Security:AuditLogKey` (base64, 32 byte) saglayin.
- Her log girdisi bir hash zinciri saklar (sequence, previous hash, hash, algorithm).
- Butunluk dogrulamasi: `GET /api/admin/audit-logs/integrity`.

## Yedekleme ve Geri Yukleme
### Yedek Olusturma
`POST /api/admin/backups`
```json
{ "includeUploads": true }
```

### Yedegi Indirme
`GET /api/admin/backups/{fileName}`

### Geri Yukleme (Once Dry Run)
`POST /api/admin/backups/restore`
```json
{ "fileName": "jurisflow-backup-YYYYMMDD-HHmmss.zip.enc", "includeUploads": true, "dryRun": true }
```

### Geri Yukleme (Uygula)
- `Backup:AllowRestore` degerini `true` yapin.
- Oneri: DB kilitlerini onlemek icin uygulamayi durdurun veya bakim moduna alin.
```json
{ "fileName": "jurisflow-backup-YYYYMMDD-HHmmss.zip.enc", "includeUploads": true, "dryRun": false }
```

### Yedek Sifreleme
- `Backup:EncryptBackups` etkinlestirin ve `Backup:EncryptionKey` (base64, 32 byte) ayarlayin.
- Sifreli yedekler `.zip.enc` olarak kaydedilir.

## Legal Hold Kilidi
- Legal hold'daki dokumanlar serbest birakilana kadar silinemez, versiyonlanamaz veya guncellenemez.
