# Supabase Faz 2 Baseline Runbook

Bu dokuman artik aktif uygulama plani degil, `arsiv / alternatif yol` niteligindedir.

Sebep:
- mevcut karar, canli Supabase prod DB'yi simdi migration baseline yoluna sokmak degil
- once tum buyuk uygulama refactorlarini bitirmek
- sonra yeni bir canonical DB semasi uretmek
- en sonda yeni Supabase ortamina cutover yapmaktir

Aktif plan icin bkz.:
- [MATTERS_NOTES_TRUST_PROD_PHASE_PLAN_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/MATTERS_NOTES_TRUST_PROD_PHASE_PLAN_TR.md:1>)

## Bu Runbook Ne Zaman Kullanilir

Yalnizca su kararlardan biri alinirse kullanilir:

1. mevcut prod Supabase DB'den resmi baseline alinmasina karar verilirse
2. yeni canonical DB yerine mevcut DB uzerinde migration zinciriyle devam edilmek istenirse
3. staging veya analiz amacli mevcut remote schema snapshot'i alinmak istenirse

## Su Anki Durum

Su an icin:
- `supabase/` klasoru repoda tutulabilir
- CLI scriptleri repoda kalabilir
- ama `db pull`, `migration repair` ve `db push` akisi aktif teslim plani degildir

## Neden Arsivde Tutuluyor

Cunku bu altyapi ileride su islerde hala faydali olabilir:
- mevcut DB'nin schema snapshot'ini almak
- karsilastirma yapmak
- legacy DB yapisini belgelemek
- alternatif migration yoluna geri donmek

## Ozet

Bu runbook silinmedi, ama aktif rota degil.

Aktif rota:
- legacy DB'yi koru
- kodu ve domain'i bitir
- final schema'yi sifirdan tasarla
- yeni Supabase DB'ye cutover yap
