using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle(WuLocker.VersionInfo.ProductName)]
[assembly: System.Reflection.AssemblyDescription(WuLocker.VersionInfo.Description)]
[assembly: System.Reflection.AssemblyConfiguration("")]
[assembly: System.Reflection.AssemblyCompany("")]
[assembly: System.Reflection.AssemblyProduct(WuLocker.VersionInfo.ProductName)]
[assembly: System.Reflection.AssemblyCopyright("")]
[assembly: System.Reflection.AssemblyTrademark("")]
[assembly: System.Reflection.AssemblyCulture("")]
[assembly: System.Reflection.AssemblyVersion(WuLocker.VersionInfo.Version)]
[assembly: System.Reflection.AssemblyFileVersion(WuLocker.VersionInfo.Version)]

namespace WuLocker
{
    public class Program
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TOKEN_PRIVILEGES_SINGLE
        {
            public int PrivilegeCount;
            public LUID Luid;
            public int Attributes;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr TokenHandle,
            bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES_SINGLE NewState,
            int BufferLength,
            IntPtr PreviousState,
            IntPtr ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const string SE_TAKE_OWNERSHIP_NAME = "SeTakeOwnershipPrivilege";
        private const string SE_BACKUP_NAME = "SeBackupPrivilege";
        private const string SE_RESTORE_NAME = "SeRestorePrivilege";

        public static bool EnablePrivileges()
        {
            IntPtr hToken;
            IntPtr hProcess = Process.GetCurrentProcess().Handle;
            if (!OpenProcessToken(hProcess, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out hToken))
            {
                return false;
            }

            try
            {
                bool success = true;
                success &= SetPrivilege(hToken, SE_TAKE_OWNERSHIP_NAME, true);
                success &= SetPrivilege(hToken, SE_BACKUP_NAME, true);
                success &= SetPrivilege(hToken, SE_RESTORE_NAME, true);
                return success;
            }
            finally
            {
                CloseHandle(hToken);
            }
        }

        private static bool SetPrivilege(IntPtr hToken, string privilegeName, bool enable)
        {
            LUID luid;
            if (!LookupPrivilegeValue(null, privilegeName, out luid))
            {
                return false;
            }

            TOKEN_PRIVILEGES_SINGLE tp = new TOKEN_PRIVILEGES_SINGLE();
            tp.PrivilegeCount = 1;
            tp.Luid = luid;
            if (enable)
            {
                tp.Attributes = 2; // SE_PRIVILEGE_ENABLED = 2
            }
            else
            {
                tp.Attributes = 0;
            }

            return AdjustTokenPrivileges(hToken, false, ref tp, Marshal.SizeOf(tp), IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("kernel32.dll", EntryPoint = "GetPrivateProfileStringW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetPrivateProfileString(
            string section, string key, string def, StringBuilder retVal, uint size, string filePath);

        private static int GetWindowsBuildNumber()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        object buildVal = key.GetValue("CurrentBuild");
                        if (buildVal != null)
                        {
                            int build;
                            if (int.TryParse(buildVal.ToString(), out build))
                            {
                                return build;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Windows build numarasi okunamadi.", ex);
            }
            return Environment.OSVersion.Version.Build; // fallback
        }

        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                RunCli(args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            OperationLog.Configure(false);

            // Fix/Ensure INI file is UTF-16LE encoded for correct Turkish character rendering
            try
            {
                string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WuLocker.ini");
                EnsureIniEncoding(iniPath);
            }
            catch (Exception ex)
            {
                OperationLog.Error("INI kodlamasi hazirlanamadi.", ex);
            }
            
            // Enable process security token privileges for registry ownership takeover
            EnablePrivileges();
            
            // Check OS Build solely (Windows 10+ Build 10240+)
            int osBuild = GetWindowsBuildNumber();
            if (osBuild < 10240)
            {
                string lang = ReadIniMain("Settings", "Language", "Auto");
                var dict = LoadLocalization(lang);

                string osMsg = "";
                if (dict.ContainsKey("OSUnsupportedError")) osMsg = dict["OSUnsupportedError"];
                if (string.IsNullOrEmpty(osMsg))
                {
                    if (IsTurkishUi())
                    {
                        osMsg = "Bu uygulama yalnızca Windows 10 ve üzeri (Build 10240+) işletim sistemlerinde çalışabilir.";
                    }
                    else
                    {
                        osMsg = "This application can only run on Windows 10 and above (Build 10240+).";
                    }
                }

                string osTitle = "";
                if (dict.ContainsKey("OSUnsupportedTitle")) osTitle = dict["OSUnsupportedTitle"];
                if (string.IsNullOrEmpty(osTitle))
                {
                    if (IsTurkishUi())
                    {
                        osTitle = "Uyumsuz İşletim Sistemi";
                    }
                    else
                    {
                        osTitle = "Incompatible OS";
                    }
                }

                MessageBox.Show(osMsg, osTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Check Admin
            if (!IsUserAnAdmin())
            {
                string lang = ReadIniMain("Settings", "Language", "Auto");
                var dict = LoadLocalization(lang);

                string adminMsg = "";
                if (dict.ContainsKey("AdminError")) adminMsg = dict["AdminError"];
                if (string.IsNullOrEmpty(adminMsg))
                {
                    if (IsTurkishUi())
                    {
                        adminMsg = "Bu uygulamayı çalıştırmak için Yönetici (Administrator) yetkileri gereklidir.";
                    }
                    else
                    {
                        adminMsg = "Administrator privileges are required to run this application.";
                    }
                }

                string adminTitle = "";
                if (dict.ContainsKey("AdminErrorTitle")) adminTitle = dict["AdminErrorTitle"];
                if (string.IsNullOrEmpty(adminTitle))
                {
                    if (IsTurkishUi())
                    {
                        adminTitle = "Yetki Hatası";
                    }
                    else
                    {
                        adminTitle = "Privilege Error";
                    }
                }

                MessageBox.Show(adminMsg, adminTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            
            Application.Run(new MainWindow());
        }

        private static bool IsUserAnAdmin()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTurkishUi()
        {
            try
            {
                return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void TrySetConsoleUtf8()
        {
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch (Exception ex)
            {
                OperationLog.Error("Konsol UTF-8 cikis kodlamasi ayarlanamadi.", ex);
            }
        }

        private static string ReadIniMain(string section, string key, string defaultValue)
        {
            string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WuLocker.ini");
            if (!File.Exists(iniPath)) return defaultValue;
            StringBuilder temp = new StringBuilder(255);
            GetPrivateProfileString(section, key, defaultValue, temp, 255, iniPath);
            return temp.ToString();
        }

        private static void EnsureIniEncoding(string path)
        {
            if (!File.Exists(path))
            {
                AppConfig.SaveDefault(path);
                return;
            }
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                // Check for UTF-16LE BOM (0xFF, 0xFE)
                if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                {
                    return; // Already UTF-16LE
                }

                // Check for UTF-8 BOM (0xEF, 0xBB, 0xBF)
                Encoding enc = Encoding.Default;
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                {
                    enc = Encoding.UTF8;
                }
                else
                {
                    if (IsValidUtf8(bytes))
                    {
                        enc = Encoding.UTF8;
                    }
                    else
                    {
                        try
                        {
                            enc = Encoding.GetEncoding(1254); // Turkish ANSI (Windows-1254)
                        }
                        catch
                        {
                            enc = Encoding.Default;
                        }
                    }
                }

                string content = enc.GetString(bytes);
                File.WriteAllText(path, content, Encoding.Unicode); // Writes as UTF-16LE with BOM
            }
            catch (Exception ex)
            {
                OperationLog.Error("INI kodlamasi donusturulemedi.", ex);
            }
        }

        private static bool IsValidUtf8(byte[] bytes)
        {
            int i = 0;
            while (i < bytes.Length)
            {
                if (bytes[i] <= 0x7F)
                {
                    i += 1;
                }
                else if (bytes[i] >= 0xC2 && bytes[i] <= 0xDF)
                {
                    if (i + 1 >= bytes.Length) return false;
                    if (bytes[i + 1] < 0x80 || bytes[i + 1] > 0xBF) return false;
                    i += 2;
                }
                else if (bytes[i] >= 0xE0 && bytes[i] <= 0xEF)
                {
                    if (i + 2 >= bytes.Length) return false;
                    if (bytes[i + 1] < 0x80 || bytes[i + 1] > 0xBF) return false;
                    if (bytes[i + 2] < 0x80 || bytes[i + 2] > 0xBF) return false;
                    i += 3;
                }
                else if (bytes[i] >= 0xF0 && bytes[i] <= 0xF4)
                {
                    if (i + 3 >= bytes.Length) return false;
                    if (bytes[i + 1] < 0x80 || bytes[i + 1] > 0xBF) return false;
                    if (bytes[i + 2] < 0x80 || bytes[i + 2] > 0xBF) return false;
                    if (bytes[i + 3] < 0x80 || bytes[i + 3] > 0xBF) return false;
                    i += 4;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        public static Dictionary<string, string> LoadLocalization(string langCode)
        {
            return LocalizationManager.Load(AppDomain.CurrentDomain.BaseDirectory, langCode);
        }

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        private static void RunCli(string[] args)
        {
            // Parse arguments
            bool block = false;
            bool enable = false;
            bool repair = false;
            bool status = false;
            bool silent = false;
            bool autoReboot = false;
            bool showHelp = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLower().TrimStart('-').TrimStart('/');
                if (arg == "b" || arg == "block") block = true;
                else if (arg == "e" || arg == "enable") enable = true;
                else if (arg == "r" || arg == "repair") repair = true;
                else if (arg == "s" || arg == "status") status = true;
                else if (arg == "y" || arg == "silent" || arg == "quiet") silent = true;
                else if (arg == "reboot") autoReboot = true;
                else if (arg == "h" || arg == "help" || arg == "?") showHelp = true;
            }

            OperationLog.Configure(silent);

            // Resolve language and localization dictionary
            string lang = ReadIniMain("Settings", "Language", "Auto");
            if (string.IsNullOrEmpty(lang) || lang.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToUpper();
            }
            var dict = LoadLocalization(lang);

            int actionCount = 0;
            if (block) actionCount++;
            if (enable) actionCount++;
            if (repair) actionCount++;
            if (status) actionCount++;

            if (actionCount > 1)
            {
                if (!silent)
                {
                    AttachConsole(-1);
                    TrySetConsoleUtf8();
                    Console.WriteLine(LocalizationManager.Get(dict, "CliSingleActionError", "Error: Please specify only one main action."));
                    FreeConsole();
                }
                Environment.Exit(1);
                return;
            }

            // If help is requested or no action is specified
            if (showHelp || (!block && !enable && !repair && !status))
            {
                AttachConsole(-1);
                TrySetConsoleUtf8();
                Console.WriteLine();

                string txtUsage = LocalizationManager.Get(dict, "CliUsage", "Usage:");
                string txtOptionsHeader = LocalizationManager.Get(dict, "CliOptionsHeader", "Options:");
                string txtOptBlock = LocalizationManager.Get(dict, "CliOptBlock", "Stops, disables and locks all Windows Updates.");
                string txtOptEnable = LocalizationManager.Get(dict, "CliOptEnable", "Unlocks and re-enables Windows Updates.");
                string txtOptRepair = LocalizationManager.Get(dict, "CliOptRepair", "Resets and repairs all Windows Update components.");
                string txtOptStatus = LocalizationManager.Get(dict, "CliOptStatus", "Queries the current update block status.");
                string txtOptSilent = LocalizationManager.Get(dict, "CliOptSilent", "Runs in silent mode (no output, no popups).");
                string txtOptReboot = LocalizationManager.Get(dict, "CliOptReboot", "Automatically restarts the system after repair.");
                string txtOptHelp = LocalizationManager.Get(dict, "CliOptHelp", "Shows this help menu.");
                string txtExitCodes = LocalizationManager.Get(dict, "CliExitCodes", "Exit Codes:");
                string txtExamples = LocalizationManager.Get(dict, "CliExamples", "Examples:");

                string exitZero = LocalizationManager.Get(dict, "CliExit0", "Updates are locked / Operation successful.");
                string exitOne = LocalizationManager.Get(dict, "CliExit1", "Updates are enabled / Operation failed or canceled.");
                string exitTwo = LocalizationManager.Get(dict, "CliExit2", "An unexpected error occurred.");

                Console.WriteLine("======================================================================");
                Console.WriteLine("     " + VersionInfo.ProductName + " v" + VersionInfo.Version + " - " + VersionInfo.Description);
                Console.WriteLine("======================================================================");
                Console.WriteLine();
                Console.WriteLine(txtUsage);
                Console.WriteLine("  WuLocker.exe [options]");
                Console.WriteLine();
                Console.WriteLine(txtOptionsHeader);
                Console.WriteLine("  -b, --block       " + txtOptBlock);
                Console.WriteLine("  -e, --enable      " + txtOptEnable);
                Console.WriteLine("  -r, --repair      " + txtOptRepair);
                Console.WriteLine("  -s, --status      " + txtOptStatus);
                Console.WriteLine("  -y, --silent      " + txtOptSilent);
                Console.WriteLine("  --reboot          " + txtOptReboot);
                Console.WriteLine("  -h, --help        " + txtOptHelp);
                Console.WriteLine();
                Console.WriteLine(txtExitCodes);
                Console.WriteLine("  0                 " + exitZero);
                Console.WriteLine("  1                 " + exitOne);
                Console.WriteLine("  2                 " + exitTwo);
                Console.WriteLine();
                Console.WriteLine(txtExamples);
                Console.WriteLine("  WuLocker.exe --block");
                Console.WriteLine("  WuLocker.exe -r --silent --reboot");
                Console.WriteLine("  WuLocker.exe --status --silent");
                Console.WriteLine("======================================================================");

                FreeConsole();
                return;
            }

            // Only attach console if NOT running in silent mode
            if (!silent)
            {
                AttachConsole(-1);
                TrySetConsoleUtf8();
                Console.WriteLine();
            }

            // Enable process token privileges for registry ownership takeover
            EnablePrivileges();

            int osBuild = GetWindowsBuildNumber();
            if (osBuild < 10240)
            {
                if (!silent)
                {
                    Console.WriteLine(LocalizationManager.Get(dict, "OSUnsupportedError", "This application can only run on Windows 10 and above (Build 10240+)."));
                    FreeConsole();
                }
                Environment.Exit(1);
                return;
            }

            // Check Admin
            if (!IsUserAnAdmin())
            {
                string adminMsg = "";
                if (dict.ContainsKey("AdminError")) adminMsg = dict["AdminError"];
                if (string.IsNullOrEmpty(adminMsg))
                {
                    if (lang == "TR")
                    {
                        adminMsg = "Hata: Bu işlemi gerçekleştirmek için Yönetici (Administrator) yetkileri gereklidir.";
                    }
                    else
                    {
                        adminMsg = "Error: Administrator privileges are required to perform this operation.";
                    }
                }
                if (!silent)
                {
                    Console.WriteLine(adminMsg);
                }
                if (!silent) FreeConsole();
                Environment.Exit(1);
                return;
            }

            // Fix/Ensure INI
            try
            {
                string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WuLocker.ini");
                EnsureIniEncoding(iniPath);
            }
            catch (Exception ex)
            {
                OperationLog.Error("CLI icin INI kodlamasi hazirlanamadi.", ex);
            }

            // Initialize LockerEngine in headless mode
            LockerEngine engine = new LockerEngine();
            engine.isHeadless = true;
            engine.isSilent = silent;

            try
            {
                if (block)
                {
                    if (!silent) Console.WriteLine(LocalizationManager.Get(dict, "CliBlocking", "Blocking Windows Updates..."));
                    engine.ExecuteBlockFromConfig();
                    if (!silent) Console.WriteLine(LocalizationManager.Get(dict, "SuccessBlock", "System Updates successfully disabled and protected!"));
                }
                else if (enable)
                {
                    if (!silent) Console.WriteLine(LocalizationManager.Get(dict, "CliEnabling", "Enabling Windows Updates..."));
                    engine.ExecuteEnable();
                    if (!silent) Console.WriteLine(LocalizationManager.Get(dict, "SuccessEnable", "System Updates successfully enabled!"));
                }
                else if (repair)
                {
                    if (!silent) Console.WriteLine(LocalizationManager.Get(dict, "CliRepairing", "Repairing and resetting Windows Update components..."));
                    engine.ExecuteRepair();
                    if (!silent) Console.WriteLine(LocalizationManager.Get(dict, "SuccessRepair", "Windows Update components have been successfully reset and repaired!"));
                    
                    if (autoReboot)
                    {
                        if (!silent) Console.WriteLine(LocalizationManager.Get(dict, "CliRebooting", "Rebooting system in 5 seconds..."));
                        OperationLog.RunProcess("shutdown.exe", "/r /t 5 /f", 5000);
                    }
                }
                else if (status)
                {
                    bool isBlocked = engine.IsUpdatesBlocked();
                    if (!silent)
                    {
                        if (isBlocked)
                        {
                            Console.WriteLine("Status: " + LocalizationManager.Get(dict, "StatusBlocked", "System Updates Disabled and Locked"));
                        }
                        else
                        {
                            Console.WriteLine("Status: " + LocalizationManager.Get(dict, "StatusActive", "System Updates Active and Running"));
                        }
                    }
                    if (!silent) FreeConsole();
                    if (isBlocked)
                    {
                        Environment.Exit(0);
                    }
                    else
                    {
                        Environment.Exit(1);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    Console.WriteLine("Error: " + ex.Message);
                }
                if (!silent) FreeConsole();
                Environment.Exit(2);
                return;
            }

            if (!silent) FreeConsole();
            Environment.Exit(0);
        }
    }
}
