# Faz 8 Production Cutover Checklist

## Freeze Oncesi

1. Staging canonical cutover basariyla gecmis olmali.
2. Faz 7 smoke raporu yesil veya aciklanan skip durumlariyla kabul edilmis olmali.
3. Prod freeze penceresi ve sorumlular netlestirilmeli.
4. Rollback karari icin zaman kutusu tanimlanmali.

## Freeze Penceresi Icinde

1. Son logical dump al.
2. Son storage backup al.
3. Gerekliyse final ETL/import kararini sabitle.
4. Render prod env farklarini iki kisi ile capraz kontrol et.
5. Canonical production blueprint referansina gore env switch planini dogrula.
6. Deploy et.
7. Smoke runner'i calistir.
8. Audit, auth, matters, trust ve billing ana akisini kontrol et.

## Cutover Sonrasi

1. Eski DB write kaynagi olmaktan cikarilmali.
2. Eski DB rollback penceresi boyunca korunmali.
3. Slow query, auth hata ve 5xx izleme panelleri yakindan izlenmeli.
4. Ilk is gunu sonunda operasyon notu yazilmali.
