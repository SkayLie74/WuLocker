# WuLocker

A lightweight, portable Windows security hardening and update control utility designed to permanently disable and lock Windows Update services, policies, and scheduled tasks to prevent automatic self-healing and forced restarts. Includes a comprehensive recovery engine and built-in interactive repair wizard.

---

## ⚠️ Important Security Warning (False Positives)

> [!WARNING]
> Because **WuLocker** performs administrative system-level modifications—such as stopping core services, modifying Service Registry key Access Control Lists (write-protection via ACL), and locking Windows Task Scheduler files via NTFS Deny rules—some antivirus engines may flag this application as a **False Positive** (PUP/Riskware).
> 
> **WuLocker does not contain any malware or telemetry.** The source code is fully open-source, and all system manipulations are performed transparently using native Windows security and Win32 APIs.

---

## Project Structure

```text
├── assets/
│   └── WuLocker.ico           # High-resolution application icon
├── locales/
│   ├── de.json, en.json, ...  # Flat JSON translation files (11 languages)
│   └── *.json                 # 100% parity across all 155 localized strings
├── src/
│   ├── AppConfig.cs           # INI configuration loader & defaults manager
│   ├── LockerEngine.cs        # Core engine: SDDL, NTFS locks, services, tasks, repair
│   ├── LocalizationManager.cs # Dynamic locale switcher with auto-detection & fallbacks
│   ├── MainWindow.cs          # Dark-mode GUI, live progress, diagnostics modal & repair wizard
│   ├── OperationLog.cs        # Thread-safe rotating logger & process runner
│   ├── VersionInfo.cs         # Assembly metadata & version attributes
│   └── WuLocker.cs            # Entry point, CLI argument parser & single-instance mutex
├── tools/
│   └── validate-locales.ps1   # PowerShell CI/CD script enforcing 100% locale key parity
├── app.manifest               # High-DPI (PerMonitorV2) & UAC elevation manifest
├── build.bat                  # Automated build script with pre-checks & zero dependencies
├── WuLocker.ini               # Dynamic configuration (services, tasks, IFEO, processes)
└── README.md                  # Project documentation
```

---

## Key Features

- **NTFS Task Locking:** Applies explicit `Deny` Access Control Entries to Windows Update scheduled task files (`UpdateOrchestrator`, `WindowsUpdate`), preventing Windows from restoring or scheduling scans.
- **SDDL Backup & Clean Restore:** Backs up original Security Descriptors (SDDL) prior to hardening and restores legitimate ACL structures with guaranteed Administrator and SYSTEM control when unlocked.
- **Service Recovery Neutralization:** Resets service failure recovery options (`sc.exe failure reset=0 actions=""`) across all configured update services (`wuauserv`, `UsoSvc`, `WaaSMedicSvc`, `dosvc`) to halt automatic self-healing.
- **Registry Hardening & WSUS Redirection:** Applies policy-level blocks (`NoAutoUpdate`, `AUOptions=1`, `DoNotConnectToWindowsUpdateInternetLocations`) and redirects update telemetry to a local closed endpoint (`127.0.0.1:8530`).
- **Interactive Repair Wizard:** Integrated repair modal supporting both Quick Component Reset (~5 seconds; services, BITS, SoftwareDistribution, Catroot2, DLL registrations) and Deep File Scan (SFC `/scannow` & DISM Component Store repair) with real-time operational logs.
- **Live Diagnostics Modal:** Real-time inspection of service statuses, startup types, registry ACL protections, task scheduler states, and pending updates.
- **High-DPI PerMonitorV2:** Clean, responsive dark-mode WinForms layout scaling seamlessly on 100% to 300% DPI monitors.
- **11 Languages (100% Parity):** English, Turkish, German, French, Spanish, Italian, Russian, Simplified Chinese, Japanese, Korean, and Portuguese. Automatically detects system display language.

---

## Compilation

WuLocker has zero external dependencies and compiles against the native Microsoft .NET Framework 4.8 C# compiler (`csc.exe`) bundled with Windows.

To build, double-click `build.bat` or run:

```powershell
.\build.bat /nopause
```

The build script automatically:
1. Validates all 11 locale JSON files for key-parity against `en.json` (155 keys).
2. Verifies the existence of application assets and manifests.
3. Compiles all `src/*.cs` modules into an optimized, self-contained `WuLocker.exe`.

---

## Command Line Interface (CLI)

WuLocker provides a complete Command Line Interface for automated enterprise deployment and scripting.

### Usage
```cmd
WuLocker.exe [options]
```

### Options
* `-b, --block`: Stop, disable, and lock all Windows Update components and tasks.
* `-e, --enable`: Unlock and re-enable Windows Update services and tasks.
* `-r, --repair`: Execute Windows Update component reset and repair sequence.
* `-s, --status`: Query the current Windows Update lock status.
* `-y, --silent`: Silent mode (suppresses interactive windows and console popups).
* `--reboot`: Automatically restarts the system 5 seconds after repair.
  > [!WARNING]
  > The reboot invokes `shutdown /r /t 5 /f`, which **force-closes all running applications without saving**. Ensure all open work is saved before running `--repair --reboot`.
* `-h, --help`: Displays the localized help menu.

### Exit Codes
* `0`: Updates are locked / Operation completed successfully.
* `1`: Updates are enabled / Operation canceled or failed.
* `2`: Unexpected runtime exception.

### Automation Examples
* **Block updates silently:**
  ```cmd
  WuLocker.exe --block --silent
  ```
* **Repair update components and reboot:**
  ```cmd
  WuLocker.exe --repair --silent --reboot
  ```
* **Query system state in PowerShell:**
  ```powershell
  & .\WuLocker.exe --status --silent
  if ($LASTEXITCODE -eq 0) { Write-Host "System is locked" } else { Write-Host "System is unlocked" }
  ```

---

<br>

# WuLocker (Türkçe)

Zorunlu güncellemeleri ve Windows'un kendi kendini otomatik onarma (self-healing) mekanizmalarını engellemek amacıyla Windows Update servislerini, kayıt defteri politikalarını ve zamanlanmış görevlerini kalıcı olarak kilitleyen, taşınabilir (portable) sistem sertleştirme ve yönetim aracıdır. Kapsamlı bir kurtarma motoru ve canlı loglu onarım sihirbazı içerir.

---

## ⚠️ Önemli Güvenlik Uyarısı (Yanlış Alarmlar / False Positives)

> [!WARNING]
> **WuLocker**; servisleri durdurma, Hizmet Kayıt Defteri ACL izinlerini değiştirme (salt-okunur koruma) ve NTFS erişim kuralları ile Görev Zamanlayıcı dosyalarını kilitleme gibi doğrudan çekirdek sistem seviyesinde işlemler gerçekleştirdiği için bazı antivirüs yazılımları bu uygulamayı **Yanlış Alarm (False Positive / Riskware)** olarak tanımlayabilir.
> 
> **WuLocker hiçbir zararlı kod, arka kapı veya izleme (telemetri) içermez.** Kaynak kod tamamen açık ve şeffaftır. Yapılan tüm müdahaleler yerel Win32 ve Windows Güvenlik API'leri kullanılarak gerçekleştirilir.

---

## Proje Mimarisi

```text
├── assets/
│   └── WuLocker.ico           # Yüksek çözünürlüklü uygulama simgesi
├── locales/
│   ├── de.json, en.json, ...  # Düz JSON yerelleştirme dosyaları (11 dil)
│   └── *.json                 # 155 anahtarın tamamında %100 parite
├── src/
│   ├── AppConfig.cs           # INI yapılandırma yöneticisi ve varsayılanlar
│   ├── LockerEngine.cs        # Çekirdek motor: SDDL, NTFS kilitleri, servisler, onarım
│   ├── LocalizationManager.cs # Otomatik dil tespiti ve dinamik JSON çeviri motoru
│   ├── MainWindow.cs          # Karanlık tema WinForms arayüzü, canlı loglar ve onarım sihirbazı
│   ├── OperationLog.cs        # Thread-safe loglama ve süreç çalıştırma motoru
│   ├── VersionInfo.cs         # Derleme ve sürüm öznitelikleri
│   └── WuLocker.cs            # Giriş noktası, CLI argüman ayrıştırıcı ve tekil oturum mutex'i
├── tools/
│   └── validate-locales.ps1   # 11 dilde anahtar eşitliğini denetleyen doğrulama betiği
├── app.manifest               # Yüksek DPI (PerMonitorV2) ve UAC yönetici yetki bildirimi
├── build.bat                  # Sıfır bağımlılıklı otomatik derleme betiği
├── WuLocker.ini               # Dinamik yapılandırma (servisler, görevler, IFEO, süreçler)
└── README.md                  # Proje belgelendirmesi
```

---

## Öne Çıkan Özellikler

- **NTFS Görev Kilitleme:** `UpdateOrchestrator` ve `WindowsUpdate` altındaki görev dosyalarına açık `Deny` kuralları yazarak Windows'un taramaları kendi kendine yeniden planlamasını ve aktifleştirmesini önler.
- **SDDL Yedekleme ve Temiz Geri Yükleme:** Kilitleme öncesinde kayıt defteri izinlerini (SDDL) yedekler; kilit açıldığında `Administrators` ve `SYSTEM` izinlerini garanti ederek izin hatalarını (Hata 1058 / Access Denied) engeller.
- **Servis İyileşme İptali:** `sc.exe failure reset=0 actions=""` çağrılarıyla yapılandırılan tüm güncelleme servislerinin (`wuauserv`, `UsoSvc`, `WaaSMedicSvc`, `dosvc`) çökme sonrası otomatik kendini başlatmasını engeller.
- **WSUS ve Politika Yönlendirmesi:** `NoAutoUpdate=1`, `AUOptions=1` kurallarını uygular ve güncelleme telemetrisini yerelde kapalı standart bir uç noktaya (`127.0.0.1:8530`) yönlendirir.
- **Etkileşimli Onarım Sihirbazı:** Hızlı Bileşen Sıfırlama (~5 saniye; servisler, BITS, SoftwareDistribution, Catroot2, DLL kütüphaneleri) ve Derin Dosya Taraması (SFC `/scannow` ve DISM Component Store onarımı) seçenekleri ile anlık işlem logları sunar.
- **Canlı Sistem Tanılama (Diagnostics):** Servis durumları, başlangıç tipleri, kayıt defteri ACL kilitleri, zamanlanmış görev izinleri ve bekleyen güncellemeleri anlık olarak raporlar.
- **Yüksek Çözünürlük (PerMonitorV2):** %100'den %300'e kadar DPI ölçeklendirmesinde bozulmayan modern karanlık arayüz.
- **11 Dil Desteği:** Türkçe, İngilizce, Almanca, Fransızca, İspanyolca, İtalyanca, Rusça, Çince, Japonca, Korece ve Portekizce.

---

## Derleme

WuLocker harici hiçbir kütüphaneye ihtiyaç duymaz; Windows ile birlikte gelen yerel Microsoft .NET Framework 4.8 derleyicisini (`csc.exe`) kullanır.

Derlemek için `build.bat` dosyasını çalıştırabilir veya terminalden şu komutu verebilirsiniz:

```powershell
.\build.bat /nopause
```

Derleme betiği:
1. 11 dil dosyasını `en.json` (155 anahtar) ile karşılaştırarak pariteyi denetler.
2. Varlık ve manifest dosyalarını kontrol eder.
3. Tüm `src/*.cs` modüllerini derleyerek kök dizinde bağımsız bir `WuLocker.exe` üretir.

---

## Komut Satırı Arayüzü (CLI)

### Kullanım
```cmd
WuLocker.exe [seçenekler]
```

### Seçenekler
* `-b, --block`: Tüm Windows Update servislerini, politikalarını ve görevlerini kilitler.
* `-e, --enable`: Güncelleme servis ve görevlerinin kilidini kaldırır, sistemi normale döndürür.
* `-r, --repair`: Windows Update bileşenlerini ve önbelleklerini baştan sıfırlar ve onarır.
* `-s, --status`: Güncel kilit durumunu sorgular.
* `-y, --silent`: Sessiz mod (ekrana konsol çıktısı veya pencere vermez).
* `--reboot`: Onarım tamamlandıktan 5 saniye sonra sistemi otomatik olarak yeniden başlatır.
  > [!WARNING]
  > Yeniden başlatma komutu `shutdown /r /t 5 /f` parametresiyle çalışır; yani **tüm çalışan programları kaydetmeden zorla kapatır**. `--repair --reboot` çalıştırmadan önce açık çalışmalarınızı kaydedin.
* `-h, --help`: Yerelleştirilmiş yardım menüsünü ekrana yazdırır.

### Çıkış Kodları (Exit Codes)
* `0`: Güncellemeler kilitli / İşlem başarıyla tamamlandı.
* `1`: Güncellemeler etkin / İşlem iptal edildi veya başarısız oldu.
* `2`: Beklenmeyen çalışma zamanı hatası.

### Örnekler
* **Sessizce güncellemeleri kilitleme:**
  ```cmd
  WuLocker.exe --block --silent
  ```
* **Sessizce onarıp yeniden başlatma:**
  ```cmd
  WuLocker.exe --repair --silent --reboot
  ```
* **PowerShell içinde durum sorgulama:**
  ```powershell
  & .\WuLocker.exe --status --silent
  if ($LASTEXITCODE -eq 0) { Write-Host "Sistem kilitli" } else { Write-Host "Sistem acik" }
  ```
