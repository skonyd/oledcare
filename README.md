# OledCare

**OLED monitörünü korumak için:** Bilgisayarın başından kalkınca ekranı kapatır, geri oturunca otomatik açar.
Normal bir USB webcam yeterli. MSI'ın OLED Care 3.0'daki "AI Care Sensor" özelliğinin yazılımla yapılmış hali.

> *English: OledCare turns your monitor off when you walk away and back on when you sit down again, using any webcam and Windows' built-in face detection. Fully offline; frames are never stored or sent anywhere.*

## Nasıl çalışır?

| Durum | Ne olur? | Simge rengi |
|---|---|---|
| **Kullanılıyor** | Klavye, fare veya kumanda kullanıyorsun. Kamera **kapalı**, kaynak tüketimi neredeyse sıfır. | 🟢 Yeşil |
| **Kontrol ediliyor** | 60 sn (ayarlanabilir) hiçbir şeye dokunmadın. Kamera açılır, saniyede bir yüz aranır. Sen ekrandaysan ekran açık kalır. | 🟠 Turuncu |
| **Ekran kapalı** | 20 sn (ayarlanabilir) yüz görülmedi. Ekran kapanır, kamera geri dönüşünü bekler. | 🔵 Mavi |
| **Geri döndün** | Kamera yüzünü art arda 2 karede görünce ya da klavye/fareye dokununca ekran açılır, kamera kapanır. | 🟢 Yeşil |
| **Beklemede** | Kamera kullanılamıyor ya da Windows oturumu kilitli. | ⚪ Gri |

**Tam ekran oyun ve videolarda da çalışır.** Örneğin oyunda uzun bir ara sahne izlerken bir şeye dokunmasan da kamera seni gördükçe ekran açık kalır, Windows'un ekran kapatma süresi de (ör. 5 dk) sıfırlanır. Kalkıp gidersen, karanlık sahnelerde yanlış kapanmasın diye biraz daha uzun bekleyip (varsayılan 60 sn) ekranı kapatır.

**Oyunlarda FPS kaybı olmaz.** Oyun oynarken sürekli tuşa bastığın için kamera hiç açılmaz. Yüz algılama ekran kartını değil işlemciyi kullanır ve sadece sen boştayken çalışır.

## Kapatma yöntemleri

Durum penceresindeki ayarlardan seçilir:

- **Siyah perde:** Tüm ekranları siyah bir pencereyle örter. Anında açılır, pencereler yerinde kalır. OLED'de siyah piksel tamamen kapalı olduğu için yanma riski yoktur.
- **Siyah perde + güç tasarrufu (varsayılan):** Önce siyah perde, 15 sn sonra monitörü güç tasarrufuna alır.
- **Sadece güç tasarrufu:** Monitör doğrudan uykuya geçer. Açılması 2-3 sn sürebilir. DisplayPort'ta bazen pencereler ana ekrana kayabilir.

> ⚠️ Webcam'i **monitörün USB girişine** takıyorsan, monitör güç tasarrufuna geçtiğinde kamera da kapanabilir ve otomatik açılma çalışmaz. Bu durumda kamerayı doğrudan bilgisayara tak ya da "Siyah perde" yöntemini kullan.

## Kurulum

1. [Releases](https://github.com/skonyd/oledcare/releases) sayfasından `OledCare-Setup-x.y.z.exe` dosyasını indir.
2. Kur. Yönetici izni gerekmez, uygulama kullanıcı klasörüne kurulur.
3. İlk açılışta Windows kamera izni isteyebilir. **Ayarlar → Gizlilik ve güvenlik → Kamera** altında "Masaüstü uygulamalarının kameraya erişmesine izin ver" seçeneğinin açık olması gerekir.

Kurmadan denemek istersen aynı sayfadaki `OledCare-Portable-x.y.z.zip` dosyasını açıp `OledCare.exe`'yi çalıştırabilirsin.

> Uygulama imzalı olmadığı için Windows SmartScreen uyarı verebilir: **Ek bilgi → Yine de çalıştır**.

## Kullanım

- Uygulama sistem tepsisinde çalışır. Simgeye **çift tıklayınca** durum penceresi açılır.
- **Durum penceresi:** Anlık durum, kamera, son yüz algılama, boşta geçen süre, uygulamanın CPU ve bellek kullanımı.
- **Düğmeler:** Başlat/Durdur, Ekranı şimdi kapat (test), Simge durumuna küçült, Çıkış.
- **Sağ tık menüsü:** Durum penceresi, izlemeyi başlat/durdur, ekranı şimdi kapat, çıkış.

### Ayarlar

| Ayar | Varsayılan | Açıklama |
|---|---|---|
| Boşta kalınca kontrole başla | 60 sn | Bu süre giriş olmazsa kamera açılır |
| Yüz görülmezse kapat | 20 sn | Kamera bu süre yüz görmezse ekran kapanır |
| Tam ekranda yüz görülmezse kapat | 60 sn | Tam ekran oyun/videoda (ara sahne vb.) kullanılan daha uzun süre |
| Kontrol aralığı | 1000 ms | Kamera kontrolleri arası süre. Seni görürken iki katına çıkar |
| Geri dönüş için gereken kare | 2 | Yanlış uyanmaları önler |
| En küçük yüz boyutu | %8 | Arkadan geçen ya da uzaktaki kişileri yok sayar |
| Ekran kapalıyken kamera süresi | 120 dk | Bu sürede dönmezsen kamera gizlilik için kapanır; ekranı klavye/fare ile açarsın |
| Tam ekranda ekranı hiç kapatma | Kapalı | Açarsan tam ekran oyun/videoda kamera kullanılmaz, ekran da kapatılmaz |
| Seni görürken ekranı açık tut | Açık | Okurken Windows'un ekranı kendiliğinden kapatmasını engeller |
| Kamera yoksa ekranı kapatma | Açık | Kamera başka bir uygulamada kilitliyse ekran kapatılmaz |
| Windows ile başlat | - | Açılışta simge durumunda başlar |

Ayarlar `%APPDATA%\OledCare\settings.json`, günlük `%APPDATA%\OledCare\oledcare.log` dosyasında tutulur.

## Gizlilik

- Tamamen çevrimdışı çalışır. İnternete hiçbir istek atmaz.
- Kamera görüntüleri **kaydedilmez ve hiçbir yere gönderilmez**. Her kare sadece bellekte yüz var mı diye kontrol edilip atılır.
- Yüz **tanıma** yapılmaz, sadece yüz **algılama** yapılır. Kim olduğun bilinmez.
- Kamera sadece sen boştayken açılır. Kamera ışığının yanması bunun göstergesidir.
- Kamera paylaşımlı okuma modunda açılır. Teams, Zoom gibi uygulamalar kamerayı kullanırken onları engellemez.

## Bilinen sınırlamalar

- Çok karanlık ortamda yüz algılanamayabilir. Bu durumda ekran beklenenden erken kapanabilir. "Yüz görülmezse kapat" süresini artırabilirsin.
- Windows'un yerleşik yüz algılayıcısı yandan veya çok eğik açıdan bakışları kaçırabilir.
- Xbox uyumlu olmayan bazı kumandaların hareketi algılanmayabilir. Kamera seni gördüğü sürece bu sorun olmaz.
- *Özel tam ekran* (exclusive fullscreen) modunda çalışan bazı eski oyunlar, üstlerine siyah perde açıldığında simge durumuna küçülebilir. Böyle bir oyunda "Sadece güç tasarrufu" yöntemini kullan ya da oyunu "kenarlıksız pencere" modunda çalıştır.

## Kaynaktan derleme

Gereksinim: [.NET 10 SDK](https://dotnet.microsoft.com/download)

```bash
dotnet build src/OledCare/OledCare.csproj -c Release
```

Tek dosyalık sürüm:

```bash
dotnet publish src/OledCare/OledCare.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Kurulum dosyası için [Inno Setup 6](https://jrsoftware.org/isinfo.php) gerekir:

```bash
iscc /DAppVersion=0.1.0 /DPublishDir=..\publish installer\OledCare.iss
```

GitHub'a `v*` etiketi gönderildiğinde (örneğin `v0.1.0`) GitHub Actions kurulum dosyasını ve taşınabilir ZIP'i otomatik oluşturup Releases sayfasına ekler.

## Teşekkürler

Fikir ve yaklaşım için incelenen projeler: [presence-guard](https://github.com/DannyOnCode/presence-guard) (yalnızca boştayken kamera açma), [eye-tracker](https://github.com/balcsida/eye-tracker) (düşük CPU tasarımı), [AreYouThere](https://github.com/nathanatgit/AreYouThere) (Windows yerleşik yüz algılayıcısı), [crysence](https://github.com/crymetr/crysence) (görüntülü görüşmeleri engellememe).

## Lisans

[MIT](LICENSE)
