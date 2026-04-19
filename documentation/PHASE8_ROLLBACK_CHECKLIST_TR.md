# Faz 8 Rollback Checklist

## Rollback Tetikleyicileri

1. `/health` stabil yesil donmuyor.
2. Staff auth veya client auth kritik oranda basarisiz.
3. Matter/trust/billing smoke blocker seviyesinde fail.
4. Veri tutarliligi veya audit butunlugu suphe doguruyor.

## Rollback Akisi

1. Render production env'i eski legacy DB baglantisina dondur.
2. Gerekliyse onceki application deploy'una don.
3. Storage tarafinda cutover ile bagli bir degisiklik varsa onceki ayarlari geri yukle.
4. Yeni canonical DB'yi silme; inceleme icin koru.
5. Incident notu ve timeline cikar.

## Rollback Sonrasi

1. Eski prod DB write kaynagi olarak tekrar teyit edilir.
2. Kullanici etkisi ve veri ayrismasi raporlanir.
3. Sonraki cutover denemesi icin blocker listesi cikarilir.
