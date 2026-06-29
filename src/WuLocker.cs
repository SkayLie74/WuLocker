using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.ServiceProcess;
using Microsoft.Win32;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Xml;

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
            if (!File.Exists(path)) return;
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
            if (block)
            {
                actionCount++;
            }
            if (enable)
            {
                actionCount++;
            }
            if (repair)
            {
                actionCount++;
            }
            if (status)
            {
                actionCount++;
            }

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

            // Initialize MainWindow in headless mode
            MainWindow main = new MainWindow();
            main.isHeadless = true;
            main.isSilent = silent;

            try
            {
                if (block)
                {
                    if (!silent) Console.WriteLine(LocalizationManager.Get(dict, "CliBlocking", "Blocking Windows Updates..."));
                    main.ExecuteBlockFromConfig();
                    if (!silent) Console.WriteLine(LocalizationManager.Get(dict, "SuccessBlock", "System Updates successfully disabled and protected!"));
                }
                else if (enable)
                {
                    if (!silent) Console.WriteLine(LocalizationManager.Get(dict, "CliEnabling", "Enabling Windows Updates..."));
                    main.ExecuteEnable();
                    if (!silent) Console.WriteLine(LocalizationManager.Get(dict, "SuccessEnable", "System Updates successfully enabled!"));
                }
                else if (repair)
                {
                    if (!silent) Console.WriteLine(LocalizationManager.Get(dict, "CliRepairing", "Repairing and resetting Windows Update components..."));
                    main.ExecuteRepair();
                    if (!silent) Console.WriteLine(LocalizationManager.Get(dict, "SuccessRepair", "Windows Update components have been successfully reset and repaired!"));
                    
                    if (autoReboot)
                    {
                        if (!silent) Console.WriteLine(LocalizationManager.Get(dict, "CliRebooting", "Rebooting system in 5 seconds..."));
                        OperationLog.RunProcess("shutdown.exe", "/r /t 5 /f", 5000);
                    }
                }
                else if (status)
                {
                    bool isBlocked = main.IsUpdatesBlocked();
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

    public class MainWindow : Form
    {
        private Panel headerPanel;
        private Label titleLabel;
        private Button closeButton;
        private Button minButton;
        
        private Panel statusCard;
        private Label statusHeaderLabel;
        private Label statusTextLabel;
        private PictureBox statusIndicator;
        
        private CustomButton btnBlock;
        private CustomButton btnEnable;
        private CustomButton btnRepair;
        private CheckBox chkProtect;
        private ComboBox comboLang;
        public bool isHeadless = false;
        public bool isSilent = false;
        
        private Timer statusTimer;
        private AppConfig config;
        
        // Target Services
        private static string[] Services = AppConfig.DefaultServices();
        private static string[] TasksToManage = AppConfig.DefaultTasks();
        private static string[] BlockProcesses = AppConfig.DefaultBlockProcesses();
        private static string[] CloseProcesses = AppConfig.DefaultCloseProcesses();
        private static string[] ImagePathServices = AppConfig.DefaultImagePathServices();
        
        // Colors
        private static readonly Color ColorBg = Color.FromArgb(15, 23, 42); // slate-900
        private static readonly Color ColorCard = Color.FromArgb(30, 41, 59); // slate-800
        private static readonly Color ColorText = Color.FromArgb(241, 245, 249); // slate-100
        private static readonly Color ColorTextMuted = Color.FromArgb(148, 163, 184); // slate-400
        private static readonly Color ColorGreen = Color.FromArgb(16, 185, 129); // emerald-500
        private static readonly Color ColorRed = Color.FromArgb(239, 68, 68); // red-500
        private static readonly Color ColorBlue = Color.FromArgb(59, 130, 246); // blue-500
        
        // Custom Dragging
        private bool dragging = false;
        private Point dragCursorPoint;
        private Point dragFormPoint;

        // INI & Localization Settings
        private string lang = "TR";
        private string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WuLocker.ini");

        // Localization Variables
        private string txtStatusChecking;
        private string txtStatusBlocked;
        private string txtStatusActive;
        private string txtStatusUnknown;
        private string txtSuccessBlock;
        private string txtSuccessEnable;
        private string txtErrorTitle;
        private string txtSuccessTitle;
        private string txtRepairingStatus;
        private string txtSuccessRepair;
        private string txtRepairReboot;
        private string txtRepairRebootPrompt;

        [DllImport("kernel32.dll", EntryPoint = "GetPrivateProfileStringW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetPrivateProfileString(
            string section, string key, string def, StringBuilder retVal, uint size, string filePath);

        [DllImport("kernel32.dll", EntryPoint = "WritePrivateProfileStringW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WritePrivateProfileString(
            string section, string key, string val, string filePath);

        private bool WriteIni(string section, string key, string value)
        {
            try
            {
                return WritePrivateProfileString(section, key, value, iniPath);
            }
            catch
            {
                return false;
            }
        }

        private string ReadIni(string section, string key, string defaultValue, int bufferSize = 255)
        {
            if (!File.Exists(iniPath)) return defaultValue;
            StringBuilder temp = new StringBuilder(bufferSize);
            GetPrivateProfileString(section, key, defaultValue, temp, (uint)bufferSize, iniPath);
            return temp.ToString();
        }

        private string[] ReadListFromIni(string section, string key, string[] defaultValues)
        {
            try
            {
                string defaultStr = string.Join(",", defaultValues);
                string val = ReadIni(section, key, defaultStr, 4096);
                if (string.IsNullOrEmpty(val)) return new string[0];
                
                string[] items = val.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < items.Length; i++)
                {
                    items[i] = items[i].Trim();
                }
                return items;
            }
            catch
            {
                return defaultValues;
            }
        }

        private void LoadDynamicLists()
        {
            config = AppConfig.Load(iniPath);
            Services = AppConfig.Clone(config.Services);
            TasksToManage = AppConfig.Clone(config.TasksToManage);
            BlockProcesses = AppConfig.Clone(config.BlockProcesses);
            CloseProcesses = AppConfig.Clone(config.CloseProcesses);
            ImagePathServices = AppConfig.Clone(config.ImagePathServices);
        }

        private int ScaleValue(int value)
        {
            try
            {
                using (Graphics graphics = this.CreateGraphics())
                {
                    double scaled = value * graphics.DpiX / 96.0;
                    return Math.Max(1, (int)Math.Round(scaled));
                }
            }
            catch
            {
                return value;
            }
        }

        public MainWindow()
        {
            LoadDynamicLists();

            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(500, 360);
            this.MinimumSize = new Size(420, 330);
            this.BackColor = ColorBg;
            this.Text = "WuLocker";
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.DoubleBuffered = true;
            
            // Set Form Icon dynamically from embedded resources
            try
            {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception ex)
            {
                OperationLog.Error("Uygulama ikonu yuklenemedi.", ex);
            }
            
            // Header
            headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = ScaleValue(42);
            headerPanel.BackColor = Color.FromArgb(10, 15, 30);
            headerPanel.MouseDown += HeaderPanel_MouseDown;
            headerPanel.MouseMove += HeaderPanel_MouseMove;
            headerPanel.MouseUp += HeaderPanel_MouseUp;

            TableLayoutPanel headerLayout = new TableLayoutPanel();
            headerLayout.Dock = DockStyle.Fill;
            headerLayout.ColumnCount = 3;
            headerLayout.RowCount = 1;
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleValue(42)));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleValue(42)));
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            headerPanel.Controls.Add(headerLayout);
            
            titleLabel = new Label();
            titleLabel.Text = VersionInfo.ProductName + " v" + VersionInfo.Version;
            titleLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            titleLabel.ForeColor = ColorText;
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.Padding = new Padding(ScaleValue(12), 0, 0, 0);
            titleLabel.AutoEllipsis = true;
            titleLabel.MouseDown += HeaderPanel_MouseDown;
            titleLabel.MouseMove += HeaderPanel_MouseMove;
            titleLabel.MouseUp += HeaderPanel_MouseUp;
            
            closeButton = new Button();
            closeButton.Text = "X";
            closeButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            closeButton.ForeColor = ColorTextMuted;
            closeButton.Dock = DockStyle.Fill;
            closeButton.Margin = new Padding(0);
            closeButton.FlatStyle = FlatStyle.Flat;
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 38, 38);
            closeButton.Click += (s, e) => this.Close();
            
            minButton = new Button();
            minButton.Text = "_";
            minButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            minButton.ForeColor = ColorTextMuted;
            minButton.Dock = DockStyle.Fill;
            minButton.Margin = new Padding(0);
            minButton.FlatStyle = FlatStyle.Flat;
            minButton.FlatAppearance.BorderSize = 0;
            minButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(51, 65, 85);
            minButton.Click += (s, e) => this.WindowState = FormWindowState.Minimized;
            
            headerLayout.Controls.Add(titleLabel, 0, 0);
            headerLayout.Controls.Add(minButton, 1, 0);
            headerLayout.Controls.Add(closeButton, 2, 0);
            this.Controls.Add(headerPanel);

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.Padding = new Padding(ScaleValue(15));
            mainLayout.BackColor = ColorBg;
            mainLayout.ColumnCount = 1;
            mainLayout.RowCount = 4;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleValue(58)));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleValue(34)));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleValue(40)));
            this.Controls.Add(mainLayout);
            headerPanel.BringToFront();
            
            // Status Card (Glassmorphism look)
            statusCard = new Panel();
            statusCard.Dock = DockStyle.Fill;
            statusCard.Margin = new Padding(0, 0, 0, ScaleValue(14));
            statusCard.Padding = new Padding(ScaleValue(14));
            statusCard.BackColor = ColorCard;
            statusCard.Paint += StatusCard_Paint;

            TableLayoutPanel statusLayout = new TableLayoutPanel();
            statusLayout.Dock = DockStyle.Fill;
            statusLayout.ColumnCount = 2;
            statusLayout.RowCount = 2;
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleValue(64)));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            statusCard.Controls.Add(statusLayout);
            
            statusIndicator = new PictureBox();
            statusIndicator.Dock = DockStyle.Fill;
            statusIndicator.Margin = new Padding(0, 0, ScaleValue(10), 0);
            statusIndicator.Paint += StatusIndicator_Paint;
            
            statusHeaderLabel = new Label();
            statusHeaderLabel.Text = "UPDATE STATUS";
            statusHeaderLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            statusHeaderLabel.ForeColor = ColorTextMuted;
            statusHeaderLabel.Dock = DockStyle.Fill;
            statusHeaderLabel.TextAlign = ContentAlignment.BottomLeft;
            statusHeaderLabel.AutoEllipsis = true;
            
            statusTextLabel = new Label();
            statusTextLabel.Text = "Checking status...";
            statusTextLabel.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold);
            statusTextLabel.ForeColor = ColorText;
            statusTextLabel.Dock = DockStyle.Fill;
            statusTextLabel.TextAlign = ContentAlignment.TopLeft;
            statusTextLabel.AutoEllipsis = true;
            
            statusLayout.Controls.Add(statusIndicator, 0, 0);
            statusLayout.SetRowSpan(statusIndicator, 2);
            statusLayout.Controls.Add(statusHeaderLabel, 1, 0);
            statusLayout.Controls.Add(statusTextLabel, 1, 1);
            mainLayout.Controls.Add(statusCard, 0, 0);
            
            // Action Buttons
            TableLayoutPanel buttonLayout = new TableLayoutPanel();
            buttonLayout.Dock = DockStyle.Fill;
            buttonLayout.ColumnCount = 3;
            buttonLayout.RowCount = 1;
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            buttonLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.Controls.Add(buttonLayout, 0, 1);

            btnBlock = new CustomButton();
            btnBlock.Text = "Updates Block (Kapat)";
            btnBlock.Dock = DockStyle.Fill;
            btnBlock.Margin = new Padding(0, 0, ScaleValue(10), ScaleValue(10));
            btnBlock.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnBlock.BackColor = ColorRed;
            btnBlock.ForeColor = Color.White;
            btnBlock.Click += BtnBlock_Click;
            
            btnEnable = new CustomButton();
            btnEnable.Text = "Updates Enable";
            btnEnable.Dock = DockStyle.Fill;
            btnEnable.Margin = new Padding(0, 0, ScaleValue(10), ScaleValue(10));
            btnEnable.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnEnable.BackColor = ColorGreen;
            btnEnable.ForeColor = Color.White;
            btnEnable.Click += BtnEnable_Click;

            btnRepair = new CustomButton();
            btnRepair.Text = "Repair";
            btnRepair.Dock = DockStyle.Fill;
            btnRepair.Margin = new Padding(0, 0, 0, ScaleValue(10));
            btnRepair.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnRepair.BackColor = ColorBlue;
            btnRepair.ForeColor = Color.White;
            btnRepair.Click += BtnRepair_Click;
            
            buttonLayout.Controls.Add(btnBlock, 0, 0);
            buttonLayout.Controls.Add(btnEnable, 1, 0);
            buttonLayout.Controls.Add(btnRepair, 2, 0);
            
            // Checkbox to protect settings
            chkProtect = new CheckBox();
            chkProtect.Text = "Lock Service Settings (Strong Protection)";
            chkProtect.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            chkProtect.ForeColor = ColorTextMuted;
            chkProtect.Dock = DockStyle.Fill;
            chkProtect.AutoSize = false;
            chkProtect.AutoEllipsis = true;
            chkProtect.Margin = new Padding(0, 0, 0, ScaleValue(8));
            mainLayout.Controls.Add(chkProtect, 0, 2);
            
            // Language Selector ComboBox
            Panel bottomPanel = new Panel();
            bottomPanel.Dock = DockStyle.Fill;
            bottomPanel.Margin = new Padding(0);
            mainLayout.Controls.Add(bottomPanel, 0, 3);

            comboLang = new ComboBox();
            comboLang.DropDownStyle = ComboBoxStyle.DropDownList;
            comboLang.FlatStyle = FlatStyle.Flat;
            comboLang.BackColor = ColorCard;
            comboLang.ForeColor = ColorText;
            comboLang.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            comboLang.Width = ScaleValue(76);
            comboLang.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            comboLang.Location = new Point(0, ScaleValue(4));
            bottomPanel.Controls.Add(comboLang);
            bottomPanel.Resize += delegate(object sender, EventArgs e)
            {
                comboLang.Left = bottomPanel.ClientSize.Width - comboLang.Width;
            };
            
            // Populate languages
            string[] supportedLangs = LocalizationManager.SupportedLanguages;
            foreach (var l in supportedLangs)
            {
                comboLang.Items.Add(l);
            }
            
            // Determine active language
            string currentLang = ReadIni("Settings", "Language", "Auto");
            if (currentLang.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                currentLang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToUpper();
            }
            else
            {
                currentLang = currentLang.ToUpper();
            }
            
            // Check list
            if (Array.IndexOf(supportedLangs, currentLang) == -1)
            {
                currentLang = "EN";
            }
            
            comboLang.SelectedItem = currentLang;
            comboLang.SelectedIndexChanged += ComboLang_SelectedIndexChanged;

            // Load and Apply INI Settings / Localization
            ApplyLocalization();

            // Load checkbox state dynamically based on actual registry lock
            LoadInitialSettings();
            
            // Status Check Timer
            statusTimer = new Timer();
            statusTimer.Interval = 2000;
            statusTimer.Tick += (s, e) => UpdateStatusGUI();
            statusTimer.Start();
            
            // Initial GUI Update
            UpdateStatusGUI();
        }

        private void ApplyLocalization()
        {
            try
            {
                EnsureConfigLoaded();
                lang = ReadIni("Settings", "Language", "Auto");
                var dict = Program.LoadLocalization(lang);

                // Load UI Labels
                this.Text = VersionInfo.ProductName;
                titleLabel.Text = VersionInfo.ProductName + " v" + VersionInfo.Version;
                
                statusHeaderLabel.Text = LocalizationManager.Get(dict, "StatusHeader", "UPDATE STATUS");
                btnBlock.Text = LocalizationManager.Get(dict, "BtnBlock", "Lock Updates (Disable)");
                btnEnable.Text = LocalizationManager.Get(dict, "BtnEnable", "Enable Updates");
                btnRepair.Text = LocalizationManager.Get(dict, "BtnRepair", "Repair Updates");
                chkProtect.Text = LocalizationManager.Get(dict, "ChkProtect", "Lock Service Settings (Strong Protection)");

                // Load Action variables
                txtStatusChecking = LocalizationManager.Get(dict, "StatusChecking", "Checking status...");
                txtStatusBlocked = LocalizationManager.Get(dict, "StatusBlocked", "System Updates Disabled and Locked");
                txtStatusActive = LocalizationManager.Get(dict, "StatusActive", "System Updates Active and Running");
                txtStatusUnknown = LocalizationManager.Get(dict, "StatusUnknown", "Status Unknown");
                txtSuccessBlock = LocalizationManager.Get(dict, "SuccessBlock", "System Updates successfully disabled and protected!");
                txtSuccessEnable = LocalizationManager.Get(dict, "SuccessEnable", "System Updates successfully enabled!");
                txtErrorTitle = LocalizationManager.Get(dict, "ErrorTitle", "Error");
                txtSuccessTitle = LocalizationManager.Get(dict, "SuccessTitle", "Success");
                txtRepairingStatus = LocalizationManager.Get(dict, "RepairingStatus", "Repairing: {0}");
                txtSuccessRepair = LocalizationManager.Get(dict, "SuccessRepair", "Windows Update components have been successfully reset and repaired!");
                txtRepairReboot = LocalizationManager.Get(dict, "RepairRebootRecommendation", "A system restart is highly recommended to complete all repairs.");
                txtRepairRebootPrompt = LocalizationManager.Get(dict, "RepairRebootPrompt", "Would you like to restart your computer now to apply the changes?");

                // Check default ini setting for check state (if registry lock check fails)
                chkProtect.Checked = config.LockServicesOnBlock;
            }
            catch (Exception ex)
            {
                OperationLog.Error("Yerellestirme uygulanamadi.", ex);
            }
        }

        private void HeaderPanel_MouseDown(object sender, MouseEventArgs e)
        {
            dragging = true;
            dragCursorPoint = Cursor.Position;
            dragFormPoint = this.Location;
        }

        private void HeaderPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                Point dif = Point.Subtract(Cursor.Position, new Size(dragCursorPoint));
                this.Location = Point.Add(dragFormPoint, new Size(dif));
            }
        }

        private void HeaderPanel_MouseUp(object sender, MouseEventArgs e)
        {
            dragging = false;
        }

        private void StatusCard_Paint(object sender, PaintEventArgs e)
        {
            // Draw a subtle border around card
            using (Pen pen = new Pen(Color.FromArgb(51, 65, 85), 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, statusCard.Width - 1, statusCard.Height - 1);
            }
        }

        private bool updatesBlocked = false;

        private void StatusIndicator_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color circleColor = ColorGreen;
            if (updatesBlocked)
            {
                circleColor = ColorRed;
            }

            int size = Math.Min(statusIndicator.ClientSize.Width, statusIndicator.ClientSize.Height);
            if (size < 8)
            {
                return;
            }

            int outerPadding = Math.Max(2, size / 20);
            int innerPadding = Math.Max(10, size / 4);
            int offsetX = (statusIndicator.ClientSize.Width - size) / 2;
            int offsetY = (statusIndicator.ClientSize.Height - size) / 2;
            Rectangle outerRect = new Rectangle(offsetX + outerPadding, offsetY + outerPadding, size - (outerPadding * 2), size - (outerPadding * 2));
            Rectangle innerRect = new Rectangle(offsetX + innerPadding, offsetY + innerPadding, size - (innerPadding * 2), size - (innerPadding * 2));
            
            // Draw outer glow circle
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(30, circleColor.R, circleColor.G, circleColor.B)))
            {
                e.Graphics.FillEllipse(brush, outerRect);
            }
            
            // Draw inner solid circle
            using (SolidBrush brush = new SolidBrush(circleColor))
            {
                e.Graphics.FillEllipse(brush, innerRect);
            }
        }

        private void UpdateStatusGUI()
        {
            try
            {
                bool isBlocked = IsUpdatesBlocked();
                updatesBlocked = isBlocked;
                
                if (isBlocked)
                {
                    statusTextLabel.Text = txtStatusBlocked;
                    statusTextLabel.ForeColor = ColorRed;
                }
                else
                {
                    statusTextLabel.Text = txtStatusActive;
                    statusTextLabel.ForeColor = ColorGreen;
                }
                statusIndicator.Invalidate();
            }
            catch
            {
                statusTextLabel.Text = txtStatusUnknown;
                statusTextLabel.ForeColor = ColorBlue;
            }
        }

        private void LoadInitialSettings()
        {
            try
            {
                chkProtect.Checked = IsRegistryLocked("wuauserv");
            }
            catch
            {
                // Leave default loaded from ini
            }
        }

        private bool IsRegistryLocked(string serviceName)
        {
            try
            {
                string registryPath = "SYSTEM\\CurrentControlSet\\Services\\" + serviceName;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions))
                {
                    if (key != null)
                    {
                        RegistrySecurity security = key.GetAccessControl();
                        return security.AreAccessRulesProtected;
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Registry kilit durumu okunamadi: " + serviceName, ex);
                return true;
            }
            return false;
        }

        public bool IsUpdatesBlocked()
        {
            EnsureConfigLoaded();

            if (!AreConfiguredServicesDisabledForStatus())
            {
                return false;
            }

            if (config.LockServicesOnBlock)
            {
                if (!AreConfiguredServiceRegistriesLockedForStatus())
                {
                    return false;
                }
            }

            if (!AreRegistryPoliciesBlockedForStatus())
            {
                return false;
            }

            if (config.SetRegNoTrayIcon)
            {
                if (!IsNoTrayIconPolicyBlockedForStatus())
                {
                    return false;
                }
            }

            if (config.SetImagePath)
            {
                if (!AreConfiguredImagePathsClearedForStatus())
                {
                    return false;
                }
            }

            if (config.SetTaskScheduler)
            {
                if (!AreConfiguredTasksBlockedForStatus())
                {
                    return false;
                }
            }

            if (config.ProcessBlockOptions)
            {
                if (!AreConfiguredProcessesBlockedForStatus())
                {
                    return false;
                }
            }

            return true;
        }

        private bool AreConfiguredServicesDisabledForStatus()
        {
            foreach (string serviceName in Services)
            {
                if (!IsServiceDisabledForStatus(serviceName))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsServiceDisabledForStatus(string serviceName)
        {
            try
            {
                string registryPath = @"SYSTEM\CurrentControlSet\Services\" + serviceName;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath, false))
                {
                    if (key == null)
                    {
                        return true;
                    }

                    object startValue = key.GetValue("Start");
                    if (startValue == null)
                    {
                        return false;
                    }

                    int start = Convert.ToInt32(startValue);
                    if (start == 4)
                    {
                        return true;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (Exception ex)
            {
                OperationLog.Error("Servis disabled durumu okunamadi: " + serviceName, ex);
                return false;
            }

            return false;
        }

        private bool AreConfiguredServiceRegistriesLockedForStatus()
        {
            foreach (string serviceName in Services)
            {
                if (!IsServiceRegistryLockedForStatus(serviceName))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsServiceRegistryLockedForStatus(string serviceName)
        {
            try
            {
                string registryPath = @"SYSTEM\CurrentControlSet\Services\" + serviceName;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions))
                {
                    if (key == null)
                    {
                        return true;
                    }

                    RegistrySecurity security = key.GetAccessControl();
                    if (security.AreAccessRulesProtected)
                    {
                        return true;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (Exception ex)
            {
                OperationLog.Error("Servis registry kilit durumu okunamadi: " + serviceName, ex);
                return false;
            }

            return false;
        }

        private bool AreRegistryPoliciesBlockedForStatus()
        {
            if (!IsRegistryDwordValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DisableWindowsUpdateAccess", 1))
            {
                return false;
            }

            if (!IsRegistryDwordValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DoNotConnectToWindowsUpdateInternetLocations", 1))
            {
                return false;
            }

            if (!IsRegistryDwordValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DisableOSUpgrade", 1))
            {
                return false;
            }

            if (!IsRegistryDwordValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1))
            {
                return false;
            }

            if (!IsRegistryDwordValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "UseWUServer", 1))
            {
                return false;
            }

            if (!IsRegistryDwordValue(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoWindowsUpdate", 1))
            {
                return false;
            }

            return true;
        }

        private bool IsNoTrayIconPolicyBlockedForStatus()
        {
            return IsRegistryDwordValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "SetDisableUXWUAccess", 1);
        }

        private bool IsRegistryDwordValue(string registryPath, string valueName, int expectedValue)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    object value = key.GetValue(valueName);
                    if (value == null)
                    {
                        return false;
                    }

                    int numericValue = Convert.ToInt32(value);
                    if (numericValue == expectedValue)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Registry DWORD durumu okunamadi: " + registryPath + "\\" + valueName, ex);
            }

            return false;
        }

        private bool AreConfiguredImagePathsClearedForStatus()
        {
            foreach (string serviceName in ImagePathServices)
            {
                if (!IsServiceImagePathClearedForStatus(serviceName))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsServiceImagePathClearedForStatus(string serviceName)
        {
            try
            {
                string registryPath = @"SYSTEM\CurrentControlSet\Services\" + serviceName;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath, false))
                {
                    if (key == null)
                    {
                        return true;
                    }

                    object imagePath = key.GetValue("ImagePath");
                    if (imagePath != null && string.IsNullOrEmpty(imagePath.ToString()))
                    {
                        return true;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (Exception ex)
            {
                OperationLog.Error("Servis ImagePath durumu okunamadi: " + serviceName, ex);
            }

            return false;
        }

        private bool AreConfiguredTasksBlockedForStatus()
        {
            string tasksDir = GetTasksDirectory();

            foreach (string task in TasksToManage)
            {
                string filePath = Path.Combine(tasksDir, task);
                if (!File.Exists(filePath))
                {
                    continue;
                }

                if (!IsTaskDisabledForStatus(filePath))
                {
                    return false;
                }

                if (!HasFileDenyRule(filePath, FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.Modify))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsTaskDisabledForStatus(string filePath)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(filePath);

                XmlNode enabledNode = doc.SelectSingleNode("//*[local-name()='Enabled']");
                if (enabledNode == null)
                {
                    return false;
                }

                if (enabledNode.InnerText.Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (Exception ex)
            {
                OperationLog.Error("Zamanlanmis gorev dosyasi okunamadi: " + filePath, ex);
            }

            return false;
        }

        private bool AreConfiguredProcessesBlockedForStatus()
        {
            string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);

            foreach (string proc in BlockProcesses)
            {
                string filePath = Path.Combine(sysDir, proc);
                if (!File.Exists(filePath))
                {
                    continue;
                }

                if (!HasFileDenyRule(filePath, FileSystemRights.ExecuteFile))
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasFileDenyRule(string filePath, FileSystemRights expectedRights)
        {
            try
            {
                FileSecurity fs = File.GetAccessControl(filePath);
                SecurityIdentifier everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                AuthorizationRuleCollection rules = fs.GetAccessRules(true, false, typeof(SecurityIdentifier));

                foreach (FileSystemAccessRule rule in rules)
                {
                    if (rule.IdentityReference == everyoneSid && rule.AccessControlType == AccessControlType.Deny)
                    {
                        FileSystemRights matchingRights = rule.FileSystemRights & expectedRights;
                        if (matchingRights != 0)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (Exception ex)
            {
                OperationLog.Error("Dosya ACL durumu okunamadi: " + filePath, ex);
            }

            return false;
        }

        private string GetTasksDirectory()
        {
            string windir = Environment.GetEnvironmentVariable("windir");
            if (string.IsNullOrEmpty(windir))
            {
                windir = "C:\\Windows";
            }

            return Path.Combine(windir, "System32", "Tasks");
        }

        private void BtnBlock_Click(object sender, EventArgs e)
        {
            this.Cursor = Cursors.WaitCursor;
            try
            {
                ExecuteBlock(chkProtect.Checked);
            }
            catch (Exception ex)
            {
                OperationLog.Error("GUI block islemi basarisiz.", ex);
                MessageBox.Show(ReadIni(lang, "ErrorTitle", "Hata") + ": " + ex.Message, txtErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = Cursors.Default;
                UpdateStatusGUI();
            }
        }

        private void BtnEnable_Click(object sender, EventArgs e)
        {
            this.Cursor = Cursors.WaitCursor;
            try
            {
                ExecuteEnable();
            }
            catch (Exception ex)
            {
                OperationLog.Error("GUI enable islemi basarisiz.", ex);
                MessageBox.Show(ReadIni(lang, "ErrorTitle", "Hata") + ": " + ex.Message, txtErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = Cursors.Default;
                UpdateStatusGUI();
            }
        }

        public void ExecuteBlock(bool protect)
        {
            EnsureConfigLoaded();
            OperationLog.Info("Bloklama islemi basladi.");

            if (config.ProcessCloseOptions)
            {
                CloseUpdateProcesses();
            }

            foreach (string svc in Services)
            {
                StopAndDisableService(svc, protect);
            }

            ApplyRegistryPolicies(true);

            if (config.SetRegNoTrayIcon)
            {
                SetRegNoTrayIcon(true);
            }

            if (config.SetTaskScheduler)
            {
                ManageScheduledTasks(false);
            }

            if (config.ProcessBlockOptions)
            {
                BlockUpdateProcesses(true);
            }

            if (config.SetImagePath)
            {
                SetServiceImagePath(true);
            }

            ClearPauseUpdates();
            OperationLog.Info("Bloklama islemi tamamlandi.");
        }

        public void ExecuteBlockFromConfig()
        {
            EnsureConfigLoaded();
            ExecuteBlock(config.LockServicesOnBlock);
        }

        public void ExecuteEnable()
        {
            EnsureConfigLoaded();
            OperationLog.Info("Etkinlestirme islemi basladi.");

            if (config.ProcessBlockOptions)
            {
                BlockUpdateProcesses(false);
            }

            foreach (string svc in Services)
            {
                UnlockRegistryKey(svc);
            }

            if (config.SetImagePath)
            {
                SetServiceImagePath(false);
            }

            foreach (string svc in Services)
            {
                RestoreAndEnableService(svc);
            }

            ApplyRegistryPolicies(false);

            if (config.SetRegNoTrayIcon)
            {
                SetRegNoTrayIcon(false);
            }

            if (config.SetTaskScheduler)
            {
                ManageScheduledTasks(true);
            }

            ClearPauseUpdates();

            if (config.PauseUpdatesOnEnable)
            {
                PauseUpdatesUntil2050();
            }

            OperationLog.Info("Etkinlestirme islemi tamamlandi.");
        }

        private void EnsureConfigLoaded()
        {
            if (config == null)
            {
                LoadDynamicLists();
            }
        }

        private void BtnRepair_Click(object sender, EventArgs e)
        {
            this.Cursor = Cursors.WaitCursor;
            EnableFormControls(false);

            System.Threading.Thread repairThread = new System.Threading.Thread(() =>
            {
                try
                {
                    ExecuteRepair();
                    this.BeginInvoke(new Action(() =>
                    {
                        this.Cursor = Cursors.Default;
                        EnableFormControls(true);
                        UpdateStatusGUI();
                        
                        DialogResult result = MessageBox.Show(
                            txtSuccessRepair + "\n\n" + txtRepairRebootPrompt, 
                            txtSuccessTitle, 
                            MessageBoxButtons.YesNo, 
                            MessageBoxIcon.Question);
                        
                        if (result == DialogResult.Yes)
                        {
                            RunProcessSilently("shutdown.exe", "/r /t 5 /f");
                        }
                    }));
                }
                catch (Exception ex)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        this.Cursor = Cursors.Default;
                        EnableFormControls(true);
                        UpdateStatusGUI();
                        MessageBox.Show(ex.Message, txtErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
            repairThread.IsBackground = true;
            repairThread.Start();
        }

        private void EnableFormControls(bool enable)
        {
            btnBlock.Enabled = enable;
            btnEnable.Enabled = enable;
            btnRepair.Enabled = enable;
            chkProtect.Enabled = enable;
            closeButton.Enabled = enable;
            minButton.Enabled = enable;
        }

        private void LogRepairStep(string stepMessage)
        {
            if (isSilent) return;
            if (isHeadless)
            {
                Console.WriteLine("Repair: " + stepMessage);
                return;
            }

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(LogRepairStep), stepMessage);
                return;
            }
            statusTextLabel.Text = string.Format(txtRepairingStatus, stepMessage);
            statusTextLabel.ForeColor = ColorBlue;
        }

        private string RepairText(string trText, string enText)
        {
            string normalized = LocalizationManager.NormalizeLanguage(lang).ToUpperInvariant();
            if (normalized == "TR")
            {
                return trText;
            }

            return enText;
        }

        private void RunProcessSilently(string fileName, string arguments)
        {
            OperationLog.RunProcess(fileName, arguments, 0);
        }

        private void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    OperationLog.Info("Dosya silindi: " + path);
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Dosya silinemedi: " + path, ex);
            }
        }

        private void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                    OperationLog.Info("Dizin silindi: " + path);
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Dizin silinemedi: " + path, ex);
            }
        }

        private void TryMoveDirectory(string sourcePath, string targetPath)
        {
            try
            {
                if (Directory.Exists(sourcePath))
                {
                    Directory.Move(sourcePath, targetPath);
                    OperationLog.Info("Dizin tasindi: " + sourcePath + " -> " + targetPath);
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Dizin tasinamadi: " + sourcePath + " -> " + targetPath, ex);
            }
        }

        private void RestoreFileDefaultAcl(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return;
                }

                OperationLog.RunProcess("icacls.exe", "\"" + filePath + "\" /reset", 10000);
            }
            catch (Exception ex)
            {
                OperationLog.Error("Dosya ACL varsayilana dondurulemedi: " + filePath, ex);
            }
        }

        private void TrySetFileOwnerToSystem(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return;
                }

                FileSecurity fs = File.GetAccessControl(filePath);
                SecurityIdentifier systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                fs.SetOwner(systemSid);
                File.SetAccessControl(filePath, fs);
                OperationLog.Info("Dosya sahipligi SYSTEM olarak ayarlandi: " + filePath);
            }
            catch (Exception ex)
            {
                OperationLog.Error("Dosya sahipligi SYSTEM olarak ayarlanamadi: " + filePath, ex);
            }
        }

        private void TryDeleteRegistryValue(RegistryKey key, string valueName)
        {
            try
            {
                if (key != null)
                {
                    key.DeleteValue(valueName, false);
                    OperationLog.Info("Registry degeri silindi: " + valueName);
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Registry degeri silinemedi: " + valueName, ex);
            }
        }

        private void TryDeleteRegistrySubTree(RegistryKey key, string subKeyName)
        {
            try
            {
                if (key != null)
                {
                    key.DeleteSubKeyTree(subKeyName, false);
                    OperationLog.Info("Registry alt anahtari silindi: " + subKeyName);
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Registry alt anahtari silinemedi: " + subKeyName, ex);
            }
        }

        public void ExecuteRepair()
        {
            // 1. Stop services
            LogRepairStep(RepairText("Servisler durduruluyor...", "Stopping services..."));
            string[] stopServices = { "wuauserv", "cryptsvc", "bits", "msiserver", "dosvc", "usosvc", "appidsvc" };
            foreach (string sName in stopServices)
            {
                try
                {
                    using (ServiceController sc = new ServiceController(sName))
                    {
                        if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                        {
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                        }
                    }
                }
                catch (Exception ex)
                {
                    OperationLog.Error("Servis durdurulamadi: " + sName, ex);
                }
            }

            // 2. Restore service registry permissions
            LogRepairStep(RepairText("Kayıt defteri izinleri sıfırlanıyor...", "Resetting registry permissions..."));
            foreach (string sName in Services)
            {
                UnlockRegistryKey(sName);
            }

            // 3. Clear BITS queue
            LogRepairStep(RepairText("BITS indirme kuyruğu temizleniyor...", "Clearing BITS download queue..."));
            try
            {
                string programData = Environment.GetEnvironmentVariable("ProgramData");
                if (string.IsNullOrEmpty(programData)) programData = "C:\\ProgramData";
                string bitsQueue = Path.Combine(programData, "Microsoft", "Network", "Downloader");
                if (Directory.Exists(bitsQueue))
                {
                    foreach (string file in Directory.GetFiles(bitsQueue, "qmgr*.dat"))
                    {
                        TryDeleteFile(file);
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("BITS indirme kuyrugu temizlenemedi.", ex);
            }

            // 4. Rename SoftwareDistribution cache folder
            LogRepairStep(RepairText("Güncelleme önbelleği temizleniyor...", "Clearing update cache..."));
            try
            {
                string windir = Environment.GetEnvironmentVariable("windir");
                if (string.IsNullOrEmpty(windir)) windir = "C:\\Windows";
                string sdPath = Path.Combine(windir, "SoftwareDistribution");
                if (Directory.Exists(sdPath))
                {
                    string sdPathOld = sdPath + ".old";
                    if (Directory.Exists(sdPathOld))
                    {
                        TryDeleteDirectory(sdPathOld);
                    }
                    TryMoveDirectory(sdPath, sdPathOld);
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("SoftwareDistribution tasinamadi.", ex);
            }

            // 5. Rename Catroot2 cache folder
            LogRepairStep(RepairText("Kriptografik önbellek temizleniyor...", "Clearing cryptographic cache..."));
            try
            {
                string windir = Environment.GetEnvironmentVariable("windir");
                if (string.IsNullOrEmpty(windir)) windir = "C:\\Windows";
                string catrootPath = Path.Combine(windir, "System32", "catroot2");
                if (Directory.Exists(catrootPath))
                {
                    string catrootPathOld = catrootPath + ".old";
                    if (Directory.Exists(catrootPathOld))
                    {
                        TryDeleteDirectory(catrootPathOld);
                    }
                    TryMoveDirectory(catrootPath, catrootPathOld);
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("catroot2 tasinamadi.", ex);
            }

            // 6. Clear Delivery Optimization cache
            LogRepairStep(RepairText("Teslim En İyileştirme önbelleği temizleniyor...", "Clearing Delivery Optimization cache..."));
            try
            {
                string programData = Environment.GetEnvironmentVariable("ProgramData");
                if (string.IsNullOrEmpty(programData)) programData = "C:\\ProgramData";
                string doCache = Path.Combine(programData, "Microsoft", "Windows", "DeliveryOptimization", "Cache");
                if (Directory.Exists(doCache))
                {
                    TryDeleteDirectory(doCache);
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Delivery Optimization onbellegi temizlenemedi.", ex);
            }

            // 6.5. Clear local Group Policy cache files
            LogRepairStep(RepairText("Grup politikası dosyaları temizleniyor...", "Clearing local Group Policy files..."));
            try
            {
                string windir = Environment.GetEnvironmentVariable("windir");
                if (string.IsNullOrEmpty(windir)) windir = "C:\\Windows";
                string gpoMachinePol = Path.Combine(windir, "System32", "GroupPolicy", "Machine", "Registry.pol");
                string gpoUserPol = Path.Combine(windir, "System32", "GroupPolicy", "User", "Registry.pol");
                TryDeleteFile(gpoMachinePol);
                TryDeleteFile(gpoUserPol);
            }
            catch (Exception ex)
            {
                OperationLog.Error("Yerel grup politikasi dosyalari temizlenemedi.", ex);
            }

            // 7. Reset Winsock, TCP/IP, DNS, and HTTP Proxy settings
            LogRepairStep(RepairText("Ağ protokolleri ve proxy sıfırlanıyor...", "Resetting network protocols and proxy..."));
            RunProcessSilently("netsh.exe", "winsock reset");
            RunProcessSilently("netsh.exe", "int ip reset");
            RunProcessSilently("ipconfig.exe", "/flushdns");
            RunProcessSilently("netsh.exe", "winhttp reset proxy");

            // 8. Re-register core Windows Update DLLs
            LogRepairStep(RepairText("Güncelleme DLL kütüphaneleri kaydediliyor...", "Registering update DLL libraries..."));
            string[] wudlls = { "wups.dll", "wups2.dll", "wuapi.dll", "wueng.dll", "wucltux.dll", "atl.dll" };
            foreach (string dll in wudlls)
            {
                RunProcessSilently("regsvr32.exe", "/s " + dll);
            }

            // 9. Run SFC scan to fix corrupted files
            LogRepairStep(RepairText("Sistem Dosyası Denetleyicisi (SFC) çalıştırılıyor...", "Running System File Checker (SFC)..."));
            RunProcessSilently("sfc.exe", "/scannow");

            // 10. Run DISM to repair component store
            LogRepairStep(RepairText("DISM Bileşen Mağazası onarılıyor...", "Running DISM Component Store repair..."));
            RunProcessSilently("dism.exe", "/online /cleanup-image /restorehealth");

            // 10.5. Repair Microsoft Store via PowerShell
            LogRepairStep(RepairText("Microsoft Store onarılıyor...", "Repairing Microsoft Store..."));
            RunProcessSilently("powershell.exe", "-ExecutionPolicy Bypass -Command \"Get-AppXPackage -AllUsers -Name Microsoft.WindowsStore | Foreach {Add-AppxPackage -DisableDevelopmentMode -Register \\\"$($_.InstallLocation)\\AppXManifest.xml\\\"}\"");

            // 11. Remove registry policy block keys
            LogRepairStep(RepairText("Grup politikaları temizleniyor...", "Clearing group policies..."));
            ApplyRegistryPolicies(false);

            // 11.5. Clean pending update and orchestrator registry states
            LogRepairStep(RepairText("Bekleyen kayıt defteri güncelleme durumları temizleniyor...", "Clearing pending update registry states..."));
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update", true))
                {
                    if (key != null)
                    {
                        TryDeleteRegistrySubTree(key, "RebootRequired");
                        TryDeleteRegistrySubTree(key, "PostRebootPending");
                    }
                }
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Services", true))
                {
                    if (key != null)
                    {
                        TryDeleteRegistrySubTree(key, "Pending");
                    }
                }
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Orchestrator", true))
                {
                    if (key != null)
                    {
                        TryDeleteRegistrySubTree(key, "Pending");
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Bekleyen Windows Update registry durumlari temizlenemedi.", ex);
            }

            // 12. Restore ImagePaths and Start Types, start services
            LogRepairStep(RepairText("Servisler yapılandırılıyor ve başlatılıyor...", "Configuring and starting services..."));
            SetRegNoTrayIcon(false);
            SetServiceImagePath(false);
            ManageScheduledTasks(true);

            foreach (string sName in Services)
            {
                RestoreAndEnableService(sName);
            }

            // Complete
            LogRepairStep(RepairText("Onarım tamamlandı!", "Repair completed!"));
        }

        private void ApplyRegistryPolicies(bool block)
        {
            try
            {
                string wpPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
                string auPath = wpPath + @"\AU";
                string explorerPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";

                if (block)
                {
                    using (RegistryKey key = Registry.LocalMachine.CreateSubKey(wpPath))
                    {
                        if (key != null)
                        {
                            key.SetValue("DisableWindowsUpdateAccess", 1, RegistryValueKind.DWord);
                            key.SetValue("DoNotConnectToWindowsUpdateInternetLocations", 1, RegistryValueKind.DWord);
                            key.SetValue("DisableOSUpgrade", 1, RegistryValueKind.DWord);
                            key.SetValue("WUServer", "localserver.localdomain.wsus", RegistryValueKind.String);
                            key.SetValue("WUStatusServer", "localserver.localdomain.wsus", RegistryValueKind.String);
                            key.SetValue("UpdateServiceUrlAlternate", "wsus.localdomain.localserver", RegistryValueKind.String);
                        }
                    }
                    using (RegistryKey key = Registry.LocalMachine.CreateSubKey(auPath))
                    {
                        if (key != null)
                        {
                            key.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
                            key.SetValue("AUOptions", 1, RegistryValueKind.DWord);
                            key.SetValue("UseWUServer", 1, RegistryValueKind.DWord);
                        }
                    }
                    using (RegistryKey key = Registry.LocalMachine.CreateSubKey(explorerPath))
                    {
                        if (key != null)
                        {
                            key.SetValue("NoWindowsUpdate", 1, RegistryValueKind.DWord);
                        }
                    }
                }
                else
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(wpPath, true))
                    {
                        if (key != null)
                        {
                            TryDeleteRegistryValue(key, "DisableWindowsUpdateAccess");
                            TryDeleteRegistryValue(key, "DoNotConnectToWindowsUpdateInternetLocations");
                            TryDeleteRegistryValue(key, "DisableOSUpgrade");
                            TryDeleteRegistryValue(key, "WUServer");
                            TryDeleteRegistryValue(key, "WUStatusServer");
                            TryDeleteRegistryValue(key, "UpdateServiceUrlAlternate");
                        }
                    }
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(auPath, true))
                    {
                        if (key != null)
                        {
                            TryDeleteRegistryValue(key, "NoAutoUpdate");
                            TryDeleteRegistryValue(key, "AUOptions");
                            TryDeleteRegistryValue(key, "UseWUServer");
                        }
                    }
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(explorerPath, true))
                    {
                        if (key != null)
                        {
                            TryDeleteRegistryValue(key, "NoWindowsUpdate");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Registry politikasi uygulanamadi.", ex);
            }
        }

        private void ChangeServiceStartType(string serviceName, string startType)
        {
            try
            {
                OperationLog.RunProcess("sc.exe", "config \"" + serviceName + "\" start= " + startType, 5000);
            }
            catch (Exception ex)
            {
                OperationLog.Error("Servis baslangic tipi degistirilemedi: " + serviceName, ex);
            }
        }

        private void StopAndDisableService(string serviceName, bool lockRegistry)
        {
            // 1. Stop service
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Servis durdurulamadi: " + serviceName, ex);
            }

            // 2. Unlock key temporarily if it was already locked to write
            UnlockRegistryKey(serviceName);

            // 3. Set Start value using sc.exe to update SCM cache.
            // All configured update-related services are disabled during block mode.
            string scType = "disabled";
            int regStartType = 4;

            ChangeServiceStartType(serviceName, scType);

            // Backup raw write to registry to be absolutely sure
            try
            {
                string registryPath = "SYSTEM\\CurrentControlSet\\Services\\" + serviceName;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath, true))
                {
                    if (key != null)
                    {
                        key.SetValue("Start", regStartType, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Servis registry Start degeri yazilamadi: " + serviceName, ex);
            }

            // 4. Lock registry DACL if requested
            if (lockRegistry)
            {
                LockRegistryKey(serviceName);
                SetServiceRecovery(serviceName, true);
            }
        }

        private void RestoreAndEnableService(string serviceName)
        {
            // 1. Unlock registry first
            UnlockRegistryKey(serviceName);
            SetServiceRecovery(serviceName, false);

            // 2. Set Start based on service specification using sc.exe to update SCM cache
            string scType = "auto"; // wuauserv, UsoSvc are Automatic (2) when enabled
            int regStartType = 2;
            if (serviceName == "WaaSMedicSvc" || serviceName == "BITS" || serviceName == "InstallService" || serviceName == "dosvc")
            {
                scType = "demand"; // WaaSMedicSvc, BITS, InstallService, and dosvc are Manual (3) when enabled
                regStartType = 3;
            }

            ChangeServiceStartType(serviceName, scType);

            // Backup raw write to registry to be absolutely sure
            try
            {
                string registryPath = "SYSTEM\\CurrentControlSet\\Services\\" + serviceName;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath, true))
                {
                    if (key != null)
                    {
                        key.SetValue("Start", regStartType, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Servis registry Start degeri geri yuklenemedi: " + serviceName, ex);
            }

            // 3. Start service
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                    {
                        sc.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Servis baslatilamadi: " + serviceName, ex);
            }
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegSetKeySecurity(
            IntPtr hKey,
            int SecurityInformation,
            byte[] pSecurityDescriptor);

        private const int OWNER_SECURITY_INFORMATION = 1;
        private const int DACL_SECURITY_INFORMATION = 4;

        private bool TakeOwnership(string registryPath)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath, RegistryRights.TakeOwnership))
                {
                    if (key != null)
                    {
                        RegistrySecurity security = new RegistrySecurity();
                        SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                        security.SetOwner(adminSid);
                        byte[] sdBytes = security.GetSecurityDescriptorBinaryForm();
                        int res = RegSetKeySecurity(key.Handle.DangerousGetHandle(), OWNER_SECURITY_INFORMATION, sdBytes);
                        return res == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Registry sahipligi alinamadi: " + registryPath, ex);
            }
            return false;
        }

        private void LockRegistryKey(string serviceName)
        {
            try
            {
                string registryPath = "SYSTEM\\CurrentControlSet\\Services\\" + serviceName;
                
                // Take ownership first
                TakeOwnership(registryPath);

                // Now restrict to read-only by removing all other rights or denying write
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath, RegistryRights.ChangePermissions))
                {
                    if (key != null)
                    {
                        RegistrySecurity acl = new RegistrySecurity();
                        acl.SetAccessRuleProtection(true, false); // Block inheritance

                        SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                        SecurityIdentifier systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                        SecurityIdentifier everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

                        // Allow only ReadKey for everyone, admin, system
                        acl.AddAccessRule(new RegistryAccessRule(everyoneSid, RegistryRights.ReadKey, AccessControlType.Allow));
                        acl.AddAccessRule(new RegistryAccessRule(adminSid, RegistryRights.ReadKey, AccessControlType.Allow));
                        acl.AddAccessRule(new RegistryAccessRule(systemSid, RegistryRights.ReadKey, AccessControlType.Allow));

                        byte[] sdBytes = acl.GetSecurityDescriptorBinaryForm();
                        RegSetKeySecurity(key.Handle.DangerousGetHandle(), DACL_SECURITY_INFORMATION, sdBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Registry anahtari kilitlenemedi: " + serviceName, ex);
            }
        }

        private void UnlockRegistryKey(string serviceName)
        {
            try
            {
                string registryPath = "SYSTEM\\CurrentControlSet\\Services\\" + serviceName;

                // Take ownership first
                TakeOwnership(registryPath);

                // Restore default full permissions for system and admin, and read-access for everyone (so services can start)
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath, RegistryRights.ChangePermissions))
                {
                    if (key != null)
                    {
                        RegistrySecurity acl = new RegistrySecurity();
                        acl.SetAccessRuleProtection(false, true); // Allow inheritance back

                        SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                        SecurityIdentifier systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                        SecurityIdentifier everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

                        acl.AddAccessRule(new RegistryAccessRule(adminSid, RegistryRights.FullControl, AccessControlType.Allow));
                        acl.AddAccessRule(new RegistryAccessRule(systemSid, RegistryRights.FullControl, AccessControlType.Allow));
                        acl.AddAccessRule(new RegistryAccessRule(everyoneSid, RegistryRights.ReadKey, AccessControlType.Allow));

                        byte[] sdBytes = acl.GetSecurityDescriptorBinaryForm();
                        RegSetKeySecurity(key.Handle.DangerousGetHandle(), DACL_SECURITY_INFORMATION, sdBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Registry anahtari acilamadi: " + serviceName, ex);
            }
        }

        // ===== TASK SCHEDULER MANAGEMENT =====

        private void ManageScheduledTasks(bool enable)
        {
            // If enabling, unlock the files first
            if (enable)
            {
                LockScheduledTasks(false);
            }

            string action = "/Disable";
            if (enable)
            {
                action = "/Enable";
            }
            foreach (string task in TasksToManage)
            {
                try
                {
                    OperationLog.RunProcess("schtasks.exe", "/Change /TN \"\\" + task + "\" " + action, 3000);
                }
                catch (Exception ex)
                {
                    OperationLog.Error("Zamanlanmis gorev yonetilemedi: " + task, ex);
                }
            }

            // If disabling, lock the files after disabling them
            if (!enable)
            {
                LockScheduledTasks(true);
            }
        }

        private void SetServiceRecovery(string serviceName, bool disable)
        {
            try
            {
                string args = "";
                if (disable)
                {
                    args = string.Format("failure \"{0}\" reset= 0 actions= \"\"", serviceName);
                }
                else
                {
                    args = string.Format("failure \"{0}\" reset= 86400 actions= restart/60000", serviceName);
                }

                OperationLog.RunProcess("sc.exe", args, 3000);
            }
            catch (Exception ex)
            {
                OperationLog.Error("Servis kurtarma ayari degistirilemedi: " + serviceName, ex);
            }
        }

        private void LockScheduledTasks(bool lockTasks)
        {
            string tasksDir = GetTasksDirectory();

            foreach (string task in TasksToManage)
            {
                try
                {
                    string filePath = Path.Combine(tasksDir, task);
                    if (!File.Exists(filePath)) continue;

                    // 1. Take ownership using Administrators SID first
                    try
                    {
                        FileSecurity fs = File.GetAccessControl(filePath);
                        SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                        fs.SetOwner(adminSid);
                        File.SetAccessControl(filePath, fs);
                    }
                    catch (Exception ex)
                    {
                        OperationLog.Error("Gorev dosyasi sahipligi alinamadi: " + filePath, ex);
                    }

                    // 2. Apply or remove Deny rule for Everyone
                    FileSecurity fs2 = File.GetAccessControl(filePath);
                    SecurityIdentifier everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                    FileSystemAccessRule denyRule = new FileSystemAccessRule(
                        everyoneSid,
                        FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.Modify,
                        AccessControlType.Deny);

                    if (lockTasks)
                    {
                        fs2.AddAccessRule(denyRule);
                    }
                    else
                    {
                        AuthorizationRuleCollection rules = fs2.GetAccessRules(true, false, typeof(SecurityIdentifier));
                        foreach (FileSystemAccessRule rule in rules)
                        {
                            if (rule.IdentityReference == everyoneSid && rule.AccessControlType == AccessControlType.Deny)
                            {
                                fs2.RemoveAccessRule(rule);
                            }
                        }
                    }
                    File.SetAccessControl(filePath, fs2);

                    if (!lockTasks)
                    {
                        RestoreFileDefaultAcl(filePath);
                        TrySetFileOwnerToSystem(filePath);
                    }
                }
                catch (Exception ex)
                {
                    OperationLog.Error("Gorev dosyasi ACL ayari uygulanamadi: " + task, ex);
                }
            }
        }

        // ===== PROCESS BLOCKING (NTFS PERMISSIONS) =====

        private void BlockUpdateProcesses(bool block)
        {
            string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            foreach (string proc in BlockProcesses)
            {
                string filePath = Path.Combine(sysDir, proc);
                if (!File.Exists(filePath)) continue;
                try
                {
                    if (block)
                    {
                        // Deny execute permission for Everyone
                        System.Security.AccessControl.FileSecurity fs = File.GetAccessControl(filePath);
                        SecurityIdentifier everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                        fs.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                            everyoneSid,
                            System.Security.AccessControl.FileSystemRights.ExecuteFile,
                            AccessControlType.Deny));
                        File.SetAccessControl(filePath, fs);
                    }
                    else
                    {
                        // Remove deny execute for Everyone
                        System.Security.AccessControl.FileSecurity fs = File.GetAccessControl(filePath);
                        SecurityIdentifier everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                        fs.RemoveAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                            everyoneSid,
                            System.Security.AccessControl.FileSystemRights.ExecuteFile,
                            AccessControlType.Deny));
                        File.SetAccessControl(filePath, fs);
                        RestoreFileDefaultAcl(filePath);
                    }
                }
                catch (Exception ex)
                {
                    OperationLog.Error("Update process dosya izni degistirilemedi: " + proc, ex);
                }
            }
        }

        // ===== PROCESS CLOSING =====

        private void CloseUpdateProcesses()
        {
            foreach (string procName in CloseProcesses)
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName(procName))
                    {
                        try
                        {
                            p.Kill();
                            OperationLog.Info("Process kapatildi: " + procName);
                        }
                        catch (Exception ex)
                        {
                            OperationLog.Error("Process kapatilamadi: " + procName, ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    OperationLog.Error("Process listesi okunamadi: " + procName, ex);
                }
            }
        }

        // ===== SET REG NO TRAY ICON =====
        private void SetRegNoTrayIcon(bool hide)
        {
            try
            {
                string path = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(path))
                {
                    if (key != null)
                    {
                        if (hide)
                            key.SetValue("SetDisableUXWUAccess", 1, RegistryValueKind.DWord);
                        else
                            TryDeleteRegistryValue(key, "SetDisableUXWUAccess");
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Windows Update tray policy uygulanamadi.", ex);
            }
        }

        // ===== SET IMAGE PATH =====

        private void SetServiceImagePath(bool nullify)
        {
            foreach (string svc in ImagePathServices)
            {
                try
                {
                    string regPath = @"SYSTEM\CurrentControlSet\Services\" + svc;
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(regPath, true))
                    {
                        if (key != null)
                        {
                            if (nullify)
                            {
                                // Save original ImagePath and set empty
                                object orig = key.GetValue("ImagePath");
                                if (orig != null && orig.ToString().Length > 0)
                                {
                                    key.SetValue("ImagePath_Backup", orig.ToString(), RegistryValueKind.ExpandString);
                                    key.SetValue("ImagePath", "", RegistryValueKind.ExpandString);
                                }
                            }
                            else
                            {
                                // Restore original ImagePath
                                object backup = key.GetValue("ImagePath_Backup");
                                if (backup != null && backup.ToString().Length > 0)
                                {
                                    key.SetValue("ImagePath", backup.ToString(), RegistryValueKind.ExpandString);
                                    TryDeleteRegistryValue(key, "ImagePath_Backup");
                                }
                                else
                                {
                                    // Failsafe recovery if backup is missing
                                    string defaultPath = "";
                                    if (svc == "wuauserv") defaultPath = @"%systemroot%\system32\svchost.exe -k netsvcs -p";
                                    else if (svc == "dosvc") defaultPath = @"%systemroot%\system32\svchost.exe -k NetworkService -p";
                                    else if (svc == "WaaSMedicSvc") defaultPath = @"%systemroot%\system32\svchost.exe -k wusvcs -p";
                                    else if (svc == "UsoSvc") defaultPath = @"%systemroot%\system32\svchost.exe -k netsvcs -p";

                                    if (defaultPath != "")
                                    {
                                        key.SetValue("ImagePath", defaultPath, RegistryValueKind.ExpandString);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OperationLog.Error("Servis ImagePath ayari degistirilemedi: " + svc, ex);
                }
            }
        }

        private void PauseUpdatesUntil2050()
        {
            try
            {
                string path = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(path))
                {
                    if (key != null)
                    {
                        string nowStr = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                        string expiryStr = "2050-12-31T23:59:59Z";

                        key.SetValue("PauseUpdatesStartTime", nowStr, RegistryValueKind.String);
                        key.SetValue("PauseUpdatesExpiryTime", expiryStr, RegistryValueKind.String);
                        key.SetValue("PauseFeatureUpdatesStartTime", nowStr, RegistryValueKind.String);
                        key.SetValue("PauseFeatureUpdatesEndTime", expiryStr, RegistryValueKind.String);
                        key.SetValue("PauseQualityUpdatesStartTime", nowStr, RegistryValueKind.String);
                        key.SetValue("PauseQualityUpdatesEndTime", expiryStr, RegistryValueKind.String);
                        key.SetValue("PauseUXState", 1, RegistryValueKind.DWord);
                        key.SetValue("FlightSettingsMaxPauseDays", 36500, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Windows Update pause ayarlari yazilamadi.", ex);
            }
        }

        private void ClearPauseUpdates()
        {
            try
            {
                string path = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path, true))
                {
                    if (key != null)
                    {
                        TryDeleteRegistryValue(key, "PauseUpdatesStartTime");
                        TryDeleteRegistryValue(key, "PauseUpdatesExpiryTime");
                        TryDeleteRegistryValue(key, "PauseFeatureUpdatesStartTime");
                        TryDeleteRegistryValue(key, "PauseFeatureUpdatesEndTime");
                        TryDeleteRegistryValue(key, "PauseQualityUpdatesStartTime");
                        TryDeleteRegistryValue(key, "PauseQualityUpdatesEndTime");
                        TryDeleteRegistryValue(key, "PauseUXState");
                        TryDeleteRegistryValue(key, "FlightSettingsMaxPauseDays");
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Windows Update pause ayarlari temizlenemedi.", ex);
            }
        }


        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Draw beautiful slate border around main form
            using (Pen pen = new Pen(Color.FromArgb(30, 41, 59), 2))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
            }
        }

        private void ComboLang_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboLang.SelectedItem != null)
            {
                string selectedLang = comboLang.SelectedItem.ToString();
                
                // Write to INI file
                WriteIni("Settings", "Language", selectedLang);
                
                // Apply immediately
                ApplyLocalization();
                
                // Also update status texts dynamically
                UpdateStatusGUI();
            }
        }
    }

    // Beautiful Modern Custom Button with Rounded Corners and Micro-Animations
    public class CustomButton : Button
    {
        private Color hoverBgColor = Color.FromArgb(51, 65, 85);
        private Color normalBgColor = Color.FromArgb(30, 41, 59);

        public CustomButton()
        {
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.Cursor = Cursors.Hand;
            this.AutoEllipsis = true;
            this.normalBgColor = this.BackColor;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            this.normalBgColor = this.BackColor;
            this.BackColor = Color.FromArgb(
                Math.Min(255, this.normalBgColor.R + 25),
                Math.Min(255, this.normalBgColor.G + 25),
                Math.Min(255, this.normalBgColor.B + 25)
            );
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            this.BackColor = this.normalBgColor;
        }
    }
}
