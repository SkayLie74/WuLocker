# WuLocker

A lightweight, portable Windows security hardening utility designed to permanently disable and lock Windows Update services and scheduled tasks to prevent automatic self-healing.

---

## ⚠️ Important Security Warning (False Positives)

> [!WARNING]
> Because **WuLocker** performs administrative system-level modifications—such as stopping services, modifying Service Registry key ACLs (write protection), and locking Windows Task Scheduler files via NTFS permissions—some antivirus engines may flag this application as a **False Positive** (PUP/Riskware).
> 
> **WuLocker does not contain any malware or telemetry.** The source code is fully open-source, and all system manipulations are performed transparently using native Windows security APIs to achieve a bulletproof update lock.

---

## Project Structure

```text
├── assets/
│   └── WuLocker.ico       # High-resolution application icon
├── locales/
│   └── *.json             # Flat JSON translation files (11 languages)
├── src/
│   └── WuLocker.cs        # Main C# source code
├── build.bat              # One-click Windows build script
├── WuLocker.exe           # Compiled portable binary
├── WuLocker.ini           # Configuration file
└── README.md              # Project documentation
```

---

## Features

- **NTFS Task Locking:** Explicitly applies `Deny` rules to Windows Update scheduled tasks, preventing the OS from re-enabling them.
- **Service Recovery Neutralization:** Resets update service recovery options to `None` to prevent self-healing.
- **Registry Write Protection:** Sets read-only permissions on update service registry entries.
- **Dynamic Settings (`WuLocker.ini`):** Define services, tasks, processes to block, and processes to close dynamically.
- **JSON Localization:** Supports 11 languages with automatic Windows system interface language detection and fallbacks.
- **High-DPI Layout:** Scaled and responsive dark-mode UI that adapts cleanly to scaling.

---

## Compilation

To compile WuLocker, simply double-click on `build.bat`, or run the following command in PowerShell:

```powershell
.\build.bat /nopause
```

The build script validates `locales/*.json`, compiles all `src\*.cs` files with UTF-8 source encoding, embeds `assets\WuLocker.ico`, and applies `app.manifest` so Windows requests administrator privileges.

---

## Command Line Interface (CLI)

WuLocker includes a powerful, localized Command Line Interface (CLI) with silent mode support. This allows IT administrators to automate system update locking across multiple machines.

### Usage
```cmd
WuLocker.exe [options]
```

### Options
* `-b, --block`: Stop, disable, and lock all Windows Updates.
* `-e, --enable`: Unlock and re-enable Windows Updates without adding a new pause period.
* `-r, --repair`: Reset and repair Windows Update components.
* `-s, --status`: Query current Windows Update block status.
* `-y, --silent`: Runs in silent mode (no output, no popups).
* `--reboot`: Automatically restarts the system 5 seconds after repair.
* `-h, --help`: Show the localized help menu.

### Exit Codes
* `0`: Updates are locked / Operation successful.
* `1`: Updates are enabled / Operation failed or canceled.
* `2`: Unexpected error occurred.

### Examples
* **Block Updates silently:**
  `WuLocker.exe --block --silent`
* **Repair and reboot silently:**
  `WuLocker.exe --repair --silent --reboot`
* **Check status inside a script:**
  `WuLocker.exe --status --silent` (Check `%errorlevel%` or `$LASTEXITCODE`)

---

# WuLocker (Türkçe)

Otomatik onarımları ve zorunlu güncellemeleri önlemek amacıyla Windows Güncelleme servislerini ve zamanlanmış görevlerini kalıcı olarak devre dışı bırakan ve kilitleyen, taşınabilir (portable) bir güvenlik aracıdır.

---

## ⚠️ Önemli Güvenlik Uyarısı (Yanlış Alarmlar / False Positives)

> [!WARNING]
> **WuLocker**; servisleri durdurma, Hizmet Kayıt Defteri anahtarı ACL izinlerini değiştirme (yazma koruması) ve NTFS izinleri aracılığıyla Windows Görev Zamanlayıcı dosyalarını kilitleme gibi doğrudan sistem seviyesinde işlemler gerçekleştirdiği için bazı antivirüs yazılımları bu uygulamayı **Yanlış Alarm (False Positive / Zararlı Değil)** olarak tanımlayabilir.
> 
> **WuLocker hiçbir zararlı kod veya izleme (telemetry) içermez.** Kaynak kod tamamen açıktır ve tüm sistem müdahaleleri, Windows güncelleştirmelerinin kesin olarak engellenmesini sağlamak için yerel Windows güvenlik API'leri kullanılarak şeffaf bir şekilde gerçekleştirilir.

---

## Proje Dizini

```text
├── assets/
│   └── WuLocker.ico       # Yüksek çözünürlüklü uygulama simgesi
├── locales/
│   └── *.json             # Düz JSON çeviri dosyaları (11 dil)
├── src/
│   └── WuLocker.cs        # Ana C# kaynak kodu
├── build.bat              # Tek tıkla Windows derleme dosyası
├── WuLocker.exe           # Derlenmiş taşınabilir program
├── WuLocker.ini           # Yapılandırma dosyası
└── README.md              # Proje belgelendirmesi
```

---

## Özellikler

- **NTFS Görev Kilitleme:** Windows Update zamanlanmış görevlerine yazma ve silme erişimini engelleyerek işletim sisteminin bu görevleri tekrar aktif etmesini önler.
- **Servis Kurtarma Önleme:** Windows Update servislerinin çökme sonrasında otomatik kendini yeniden başlatma ayarlarını iptal eder.
- **Kayıt Defteri Yazma Koruması:** Güncelleme servislerinin kayıt defteri girdilerini salt okunur hale getirir.
- **Dinamik Yapılandırma (`WuLocker.ini`):** Engellenecek servisleri, görevleri ve işlemleri dinamik olarak tanımlamanıza olanak tanır.
- **JSON Dil Desteği:** 11 dil desteği ile Windows sistem arayüz dilini otomatik tespit ederek uygun yerelleştirmeyi yükler.
- **Yüksek Çözünürlük (DPI) Desteği:** Yüksek ekran ölçeklendirmelerinde metinlerin sığmasını sağlayan modern karanlık arayüz tasarımı.

---

## Derleme

WuLocker'ı derlemek için `build.bat` dosyasına çift tıklayabilir veya PowerShell üzerinde şu komutu çalıştırabilirsiniz:

```powershell
.\build.bat /nopause
```

Derleme betiği `locales/*.json` dosyalarını doğrular, tüm `src\*.cs` dosyalarını UTF-8 kaynak kodlamasıyla derler, `assets\WuLocker.ico` simgesini ve yönetici yetkisi isteyen `app.manifest` dosyasını uygular.

---

## Komut Satırı Arayüzü (CLI)

WuLocker, sessiz mod desteğine sahip gelişmiş ve yerelleştirilmiş bir Komut Satırı Arayüzü (CLI) içerir. Bu sayede sistem yöneticileri güncellemeleri toplu olarak kolayca yönetebilir.

### Kullanım
```cmd
WuLocker.exe [seçenekler]
```

### Seçenekler
* `-b, --block`: Windows güncellemelerini durdurur, devre dışı bırakır ve kilitler.
* `-e, --enable`: Windows güncellemelerinin kilidini açar ve yeni duraklatma süresi eklemeden etkinleştirir.
* `-r, --repair`: Tüm Windows Update bileşenlerini sıfırlar ve onarır.
* `-s, --status`: Güncel engelleme durumunu sorgular.
* `-y, --silent`: Sessiz mod. Ekrana hiçbir konsol çıktısı veya pop-up vermez.
* `--reboot`: Onarım bittiğinde sistemi otomatik olarak 5 saniye içinde yeniden başlatır.
* `-h, --help`: Yerelleştirilmiş yardım menüsünü gösterir.

### Çıkış Kodları (Exit Codes)
* `0`: Güncellemeler kilitli / İşlem başarılı.
* `1`: Güncellemeler etkin / İşlem başarısız veya iptal.
* `2`: Beklenmeyen bir hata oluştu.

### Örnekler
* **Güncellemeleri sessizce kilitleme:**
  `WuLocker.exe --block --silent`
* **Sessizce onarıp yeniden başlatma:**
  `WuLocker.exe --repair --silent --reboot`
* **Script içinde durum sorgulama:**
  `WuLocker.exe --status --silent` (Dönen `%errorlevel%` veya `$LASTEXITCODE` değerini kontrol edin)
