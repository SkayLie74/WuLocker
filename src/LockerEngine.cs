using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Xml;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace WuLocker
{
    public class ComponentStatusReport
    {
        public bool OverallBlocked;
        public Dictionary<string, bool> Services = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> RegistryKeys = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> Policies = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> Tasks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> Processes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public bool ImagePathsCleared;
    }

    public class LockerEngine
    {
        public bool isHeadless = false;
        public bool isSilent = false;
        private AppConfig config;
        private string iniPath;

        public AppConfig Config
        {
            get { return config; }
        }

        public string IniPath
        {
            get { return iniPath; }
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegSetKeySecurity(
            IntPtr hKey,
            int SecurityInformation,
            byte[] pSecurityDescriptor);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int RegOpenKeyEx(
            IntPtr hKey,
            string lpSubKey,
            int ulOptions,
            int samDesired,
            out IntPtr phkResult);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            UIntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out UIntPtr lpdwResult);

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private const int OWNER_SECURITY_INFORMATION = 1;
        private const int DACL_SECURITY_INFORMATION = 4;
        private static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(unchecked((int)0x80000002));
        private const int REG_OPTION_BACKUP_RESTORE = 0x00000004;
        private const int KEY_ALL_ACCESS = 0x000F003F;
        private const int WRITE_DAC = 0x00040000;
        private const int WRITE_OWNER = 0x00080000;
        private const int READ_CONTROL = 0x00020000;

        public static RegistryKey OpenLocalMachineKeyWithRestore(string subKeyPath, bool writable)
        {
            Program.EnablePrivileges();
            IntPtr hKey = IntPtr.Zero;
            int samDesired = writable 
                ? (KEY_ALL_ACCESS | WRITE_DAC | WRITE_OWNER) 
                : (0x20019 | READ_CONTROL);

            int res = RegOpenKeyEx(HKEY_LOCAL_MACHINE, subKeyPath, REG_OPTION_BACKUP_RESTORE, samDesired, out hKey);
            if (res == 0 && hKey != IntPtr.Zero)
            {
                return RegistryKey.FromHandle(new SafeRegistryHandle(hKey, true));
            }
            return null;
        }

        public LockerEngine(string customIniPath = null)
        {
            if (string.IsNullOrEmpty(customIniPath))
            {
                iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WuLocker.ini");
            }
            else
            {
                iniPath = customIniPath;
            }
            ReloadConfig();
        }

        public void ReloadConfig()
        {
            config = AppConfig.Load(iniPath);
        }

        public void EnsureConfigLoaded()
        {
            if (config == null)
            {
                ReloadConfig();
            }
        }

        private bool IsServiceExcludedForStore(string serviceName)
        {
            if (config != null && config.AllowMicrosoftStore)
            {
                if (serviceName.Equals("InstallService", StringComparison.OrdinalIgnoreCase) ||
                    serviceName.Equals("BITS", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // ================= STATUS CHECKS =================

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

        public bool AreConfiguredServicesDisabledForStatus()
        {
            foreach (string serviceName in config.Services)
            {
                if (IsServiceExcludedForStore(serviceName))
                {
                    continue;
                }

                if (!IsServiceDisabledForStatus(serviceName))
                {
                    return false;
                }
            }
            return true;
        }

        public bool IsServiceDisabledForStatus(string serviceName)
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

        public bool AreConfiguredServiceRegistriesLockedForStatus()
        {
            foreach (string serviceName in config.Services)
            {
                if (IsServiceExcludedForStore(serviceName))
                {
                    continue;
                }

                if (!IsServiceRegistryLockedForStatus(serviceName))
                {
                    return false;
                }
            }
            return true;
        }

        public bool IsServiceRegistryLockedForStatus(string serviceName)
        {
            try
            {
                string registryPath = @"SYSTEM\CurrentControlSet\Services\" + serviceName;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath, RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadKey))
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

                using (RegistryKey writeKey = Registry.LocalMachine.OpenSubKey(registryPath, true))
                {
                    if (writeKey == null)
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

        public bool AreRegistryPoliciesBlockedForStatus()
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

        public bool IsNoTrayIconPolicyBlockedForStatus()
        {
            return IsRegistryDwordValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "SetDisableUXWUAccess", 1);
        }

        public bool IsRegistryDwordValue(string registryPath, string valueName, int expectedValue)
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

                    int intVal;
                    if (int.TryParse(value.ToString(), out intVal))
                    {
                        return intVal == expectedValue;
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Registry DWORD degeri okunamadi: " + registryPath + "/" + valueName, ex);
            }

            return false;
        }

        public bool AreConfiguredImagePathsClearedForStatus()
        {
            foreach (string serviceName in config.ImagePathServices)
            {
                if (IsServiceExcludedForStore(serviceName))
                {
                    continue;
                }

                if (!IsServiceImagePathClearedForStatus(serviceName))
                {
                    return false;
                }
            }

            return true;
        }

        public bool IsServiceImagePathClearedForStatus(string serviceName)
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

        public bool AreConfiguredTasksBlockedForStatus()
        {
            string tasksDir = GetTasksDirectory();

            foreach (string task in config.TasksToManage)
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

        public bool IsTaskDisabledForStatus(string filePath)
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

        public bool AreConfiguredProcessesBlockedForStatus()
        {
            string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);

            foreach (string proc in config.BlockProcesses)
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

        public bool HasFileDenyRule(string filePath, FileSystemRights expectedRights)
        {
            try
            {
                FileSecurity fs = new FileSecurity(filePath, AccessControlSections.Access);
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
            catch (Exception ex)
            {
                OperationLog.Error("Dosya ACL durumu okunamadi: " + filePath, ex);
            }

            return false;
        }

        public string GetTasksDirectory()
        {
            string windir = Environment.GetEnvironmentVariable("windir");
            if (string.IsNullOrEmpty(windir))
            {
                windir = "C:\\Windows";
            }
            return Path.Combine(windir, "System32", "Tasks");
        }

        public ComponentStatusReport GetStatusReport()
        {
            EnsureConfigLoaded();
            ComponentStatusReport report = new ComponentStatusReport();
            report.OverallBlocked = IsUpdatesBlocked();

            foreach (string svc in config.Services)
            {
                if (IsServiceExcludedForStore(svc)) continue;
                report.Services[svc] = IsServiceDisabledForStatus(svc);
                report.RegistryKeys[svc] = IsServiceRegistryLockedForStatus(svc);
            }

            report.Policies["DisableWindowsUpdateAccess"] = IsRegistryDwordValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DisableWindowsUpdateAccess", 1);
            report.Policies["NoAutoUpdate"] = IsRegistryDwordValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1);
            report.Policies["UseWUServer"] = IsRegistryDwordValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "UseWUServer", 1);
            report.Policies["NoWindowsUpdate"] = IsRegistryDwordValue(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoWindowsUpdate", 1);

            string tasksDir = GetTasksDirectory();
            foreach (string t in config.TasksToManage)
            {
                string fp = Path.Combine(tasksDir, t);
                if (File.Exists(fp))
                {
                    report.Tasks[Path.GetFileName(t)] = IsTaskDisabledForStatus(fp) && HasFileDenyRule(fp, FileSystemRights.Write);
                }
            }

            string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            foreach (string p in config.BlockProcesses)
            {
                string fp = Path.Combine(sysDir, p);
                if (File.Exists(fp))
                {
                    report.Processes[p] = HasFileDenyRule(fp, FileSystemRights.ExecuteFile);
                }
            }

            report.ImagePathsCleared = AreConfiguredImagePathsClearedForStatus();
            return report;
        }

        // ================= BLOCK & ENABLE ACTIONS =================

        public void ExecuteBlock(bool protect)
        {
            EnsureConfigLoaded();
            OperationLog.Info("Bloklama islemi basladi. StoreModu=" + config.AllowMicrosoftStore.ToString());

            if (config.ProcessCloseOptions)
            {
                CloseUpdateProcesses();
            }

            foreach (string svc in config.Services)
            {
                if (IsServiceExcludedForStore(svc))
                {
                    OperationLog.Info("Store modu aktif oldugu icin servis atlandi: " + svc);
                    continue;
                }
                StopAndDisableServiceInternal(svc);
            }

            foreach (string svc in config.Services)
            {
                if (IsServiceExcludedForStore(svc)) continue;
                SetServiceRecovery(svc, true);
            }

            if (config.SetImagePath)
            {
                SetServiceImagePath(true);
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

            ClearPauseUpdates();

            if (protect)
            {
                foreach (string svc in config.Services)
                {
                    if (IsServiceExcludedForStore(svc)) continue;
                    LockRegistryKey(svc);
                }
            }

            RefreshSystemPolicyCache();
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

            foreach (string svc in config.Services)
            {
                UnlockRegistryKey(svc);
            }

            if (config.SetImagePath)
            {
                SetServiceImagePath(false);
            }

            ApplyRegistryPolicies(false);
            ClearPauseUpdates();

            if (config.SetRegNoTrayIcon)
            {
                SetRegNoTrayIcon(false);
            }

            if (config.SetTaskScheduler)
            {
                ManageScheduledTasks(true);
            }

            foreach (string svc in config.Services)
            {
                SetServiceRecovery(svc, false);
            }

            CleanUpdatePendingStates();
            ClearUpdateCacheFiles();
            RegisterUpdateDlls();
            ResetUpdateNetwork();

            try
            {
                using (ServiceController scCrypt = new ServiceController("cryptsvc"))
                {
                    if (scCrypt.Status != ServiceControllerStatus.Running && scCrypt.Status != ServiceControllerStatus.StartPending)
                    {
                        scCrypt.Start();
                    }
                }
            }
            catch { }

            foreach (string svc in config.Services)
            {
                RestoreAndEnableService(svc);
            }

            if (config.PauseUpdatesOnEnable)
            {
                PauseUpdatesUntil2050();
            }

            RefreshSystemPolicyCache();

            try
            {
                OperationLog.RunProcess("usoclient.exe", "RefreshSettings", 2000);
                OperationLog.RunProcess("usoclient.exe", "StartScan", 2000);
            }
            catch { }

            OperationLog.Info("Etkinlestirme islemi tamamlandi.");
        }

        public void CleanUpdatePendingStates()
        {
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
        }

        public void ClearUpdateCacheFiles()
        {
            try
            {
                string windir = Environment.GetEnvironmentVariable("windir");
                if (string.IsNullOrEmpty(windir)) windir = @"C:\Windows";
                string sdPath = Path.Combine(windir, "SoftwareDistribution");
                if (Directory.Exists(sdPath))
                {
                    string dataStore = Path.Combine(sdPath, "DataStore");
                    if (Directory.Exists(dataStore))
                    {
                        try
                        {
                            foreach (string f in Directory.GetFiles(dataStore))
                            {
                                TryDeleteFile(f);
                            }
                        }
                        catch { }
                    }
                }

                string programData = Environment.GetEnvironmentVariable("ProgramData");
                if (string.IsNullOrEmpty(programData)) programData = @"C:\ProgramData";
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
                OperationLog.Error("Guncelleme onbellek dosyalari temizlenemedi.", ex);
            }
        }

        public void RegisterUpdateDlls()
        {
            string[] wudlls = { "wups.dll", "wups2.dll", "wuapi.dll", "wueng.dll", "atl.dll" };
            foreach (string dll in wudlls)
            {
                try
                {
                    OperationLog.RunProcess("regsvr32.exe", "/s " + dll, 3000);
                }
                catch { }
            }
        }

        public void ResetUpdateNetwork()
        {
            try
            {
                OperationLog.RunProcess("netsh.exe", "winhttp reset proxy", 3000);
            }
            catch { }
        }

        public static void RefreshSystemPolicyCache()
        {
            try
            {
                // Arka planda yerel grup ilkesini tazele (UI veya islem is parcacigini dondurmez)
                try
                {
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        try { OperationLog.RunProcess("gpupdate.exe", "/target:computer /force", 5000); } catch { }
                    });
                }
                catch { }

                UIntPtr result;
                SendMessageTimeout((IntPtr)0xffff, 0x001A, UIntPtr.Zero, "Policy", 0x0002, 1000, out result);
                SendMessageTimeout((IntPtr)0xffff, 0x001A, UIntPtr.Zero, "Environment", 0x0002, 1000, out result);

                SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                OperationLog.Error("Policy onbellegi tazelenemedi.", ex);
            }
        }

        public void StopAndDisableServiceInternal(string serviceName)
        {
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

            UnlockRegistryKey(serviceName);

            string scType = "disabled";
            int regStartType = 4;
            if (serviceName != "WaaSMedicSvc")
            {
                ChangeServiceStartType(serviceName, scType);
            }

            try
            {
                string registryPath = "SYSTEM\\CurrentControlSet\\Services\\" + serviceName;
                using (RegistryKey key = OpenLocalMachineKeyWithRestore(registryPath, true) ?? Registry.LocalMachine.OpenSubKey(registryPath, true))
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
                try
                {
                    OperationLog.RunProcess("reg.exe", "add \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\" + serviceName + "\" /v Start /t REG_DWORD /d " + regStartType + " /f", 3000);
                }
                catch { }
            }
        }

        public void StopAndDisableService(string serviceName, bool lockRegistry)
        {
            StopAndDisableServiceInternal(serviceName);
            SetServiceRecovery(serviceName, true);

            if (lockRegistry)
            {
                LockRegistryKey(serviceName);
            }
        }

        public void RestoreAndEnableService(string serviceName)
        {
            UnlockRegistryKey(serviceName);
            SetServiceRecovery(serviceName, false);

            string scType = "auto";
            int regStartType = 2;
            if (serviceName == "WaaSMedicSvc" || serviceName == "BITS" || serviceName == "InstallService" || serviceName == "dosvc")
            {
                scType = "demand";
                regStartType = 3;
            }

            // WaaSMedicSvc is hardened by Windows OS; SCM API rejects sc.exe config with Error 5 (Access is denied).
            // It must be restored via registry directly.
            if (serviceName != "WaaSMedicSvc")
            {
                ChangeServiceStartType(serviceName, scType);
            }

            // Resolve Error 1290 (ERROR_SERVICE_NOT_IN_EXE):
            // On Windows 10/11 wuauserv has ServiceSidType=unrestricted. If placed in a shared svchost process,
            // SCM rejects starting it due to SID mismatch with other services in that host.
            // Configuring type= own isolates the service into its own svchost process.
            if (serviceName == "wuauserv")
            {
                OperationLog.RunProcess("sc.exe", "config wuauserv type= own", 3000);
                OperationLog.RunProcess("sc.exe", "sidtype wuauserv unrestricted", 3000);
            }
            else if (serviceName == "UsoSvc")
            {
                OperationLog.RunProcess("sc.exe", "config UsoSvc type= own", 3000);
            }
            else if (serviceName == "BITS")
            {
                OperationLog.RunProcess("sc.exe", "config BITS type= own", 3000);
            }

            try
            {
                string registryPath = "SYSTEM\\CurrentControlSet\\Services\\" + serviceName;
                using (RegistryKey key = OpenLocalMachineKeyWithRestore(registryPath, true) ?? Registry.LocalMachine.OpenSubKey(registryPath, true))
                {
                    if (key != null)
                    {
                        key.SetValue("Start", regStartType, RegistryValueKind.DWord);

                        if (serviceName == "wuauserv")
                        {
                            key.SetValue("Type", 16, RegistryValueKind.DWord); // 0x10 = SERVICE_WIN32_OWN_PROCESS
                            key.SetValue("ServiceSidType", 1, RegistryValueKind.DWord); // 1 = SERVICE_SID_TYPE_UNRESTRICTED
                            key.SetValue("ObjectName", "LocalSystem", RegistryValueKind.String);
                            key.SetValue("ImagePath", @"%systemroot%\system32\svchost.exe -k netsvcs -p", RegistryValueKind.ExpandString);
                        }
                        else if (serviceName == "UsoSvc")
                        {
                            key.SetValue("Type", 16, RegistryValueKind.DWord);
                            key.SetValue("ServiceSidType", 1, RegistryValueKind.DWord);
                            key.SetValue("ImagePath", @"%systemroot%\system32\svchost.exe -k netsvcs -p", RegistryValueKind.ExpandString);
                        }
                        else if (serviceName == "WaaSMedicSvc")
                        {
                            key.SetValue("Start", 3, RegistryValueKind.DWord);
                            key.SetValue("ServiceSidType", 1, RegistryValueKind.DWord);
                            key.SetValue("ImagePath", @"%systemroot%\system32\svchost.exe -k wusvcs -p", RegistryValueKind.ExpandString);
                        }
                        else if (serviceName == "BITS")
                        {
                            key.SetValue("Type", 16, RegistryValueKind.DWord);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Servis registry Start degeri geri yuklenemedi: " + serviceName, ex);
                try
                {
                    OperationLog.RunProcess("reg.exe", "add \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\" + serviceName + "\" /v Start /t REG_DWORD /d " + regStartType + " /f", 3000);
                }
                catch { }
            }

            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                    {
                        sc.Start();
                    }

                    if (serviceName == "wuauserv" || serviceName == "UsoSvc")
                    {
                        try
                        {
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(3));
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Servis baslatilamadi: " + serviceName, ex);
                try
                {
                    if (serviceName == "wuauserv")
                    {
                        OperationLog.RunProcess("taskkill.exe", "/f /fi \"SERVICES eq wuauserv\"", 3000);
                        System.Threading.Thread.Sleep(500);
                    }
                    OperationLog.RunProcess("sc.exe", "start \"" + serviceName + "\"", 3000);
                }
                catch { }
            }
        }

        public void ApplyRegistryPolicies(bool block)
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
                            // K-4: WU istemcisi gecerli bir URL semasi ve port bekler (port 0 gecersiz sayilir; 8530 standart WSUS portudur)
                            key.SetValue("WUServer", "http://127.0.0.1:8530", RegistryValueKind.String);
                            key.SetValue("WUStatusServer", "http://127.0.0.1:8530", RegistryValueKind.String);
                            key.SetValue("UpdateServiceUrlAlternate", "http://127.0.0.1:8530", RegistryValueKind.String);
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
                            TryDeleteRegistryValue(key, "SetDisableUXWUAccess");
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

        public void ChangeServiceStartType(string serviceName, string startType)
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

        public bool TakeOwnership(string registryPath)
        {
            try
            {
                Program.EnablePrivileges();
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath, RegistryKeyPermissionCheck.Default, RegistryRights.TakeOwnership))
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

        public void LockRegistryKey(string serviceName)
        {
            try
            {
                string registryPath = "SYSTEM\\CurrentControlSet\\Services\\" + serviceName;

                // K-3: kilit öncesi orijinal güvenlik tanımlayıcısını yedekle; açarken gerçek default geri yüklensin
                BackupRegistrySecurity(serviceName, registryPath);

                using (RegistryKey key = OpenLocalMachineKeyWithRestore(registryPath, true) ?? Registry.LocalMachine.OpenSubKey(registryPath, RegistryKeyPermissionCheck.Default, RegistryRights.ChangePermissions))
                {
                    if (key != null)
                    {
                        RegistrySecurity acl = new RegistrySecurity();
                        acl.SetAccessRuleProtection(true, false);

                        SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                        SecurityIdentifier systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                        SecurityIdentifier everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

                        acl.AddAccessRule(new RegistryAccessRule(everyoneSid, RegistryRights.ReadKey, AccessControlType.Allow));
                        acl.AddAccessRule(new RegistryAccessRule(systemSid, RegistryRights.ReadKey, AccessControlType.Allow));
                        acl.AddAccessRule(new RegistryAccessRule(adminSid, RegistryRights.ReadKey | RegistryRights.ChangePermissions, AccessControlType.Allow));

                        key.SetAccessControl(acl);
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Registry anahtari kilitlenemedi: " + serviceName, ex);
            }
        }

        public void UnlockRegistryKey(string serviceName)
        {
            try
            {
                string registryPath = "SYSTEM\\CurrentControlSet\\Services\\" + serviceName;

                SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                SecurityIdentifier systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                SecurityIdentifier usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                SecurityIdentifier appPkgSid = new SecurityIdentifier("S-1-15-2-1");

                RegistrySecurity acl = new RegistrySecurity();
                acl.SetAccessRuleProtection(false, true);
                acl.AddAccessRule(new RegistryAccessRule(adminSid, RegistryRights.FullControl, AccessControlType.Allow));
                acl.AddAccessRule(new RegistryAccessRule(systemSid, RegistryRights.FullControl, AccessControlType.Allow));
                acl.AddAccessRule(new RegistryAccessRule(usersSid, RegistryRights.ReadKey, AccessControlType.Allow));
                acl.AddAccessRule(new RegistryAccessRule(appPkgSid, RegistryRights.ReadKey, AccessControlType.Allow));

                // K-3: SDDL yedeği varsa gerçek default'u oradan geri yükle ( ağır şablon ACL yerine )
                bool restoredFromBackup = TryRestoreFromSddlBackup(serviceName);
                if (restoredFromBackup)
                {
                    OperationLog.Info("Registry anahtari acildi (yedekten): " + serviceName);
                    return;
                }

                using (RegistryKey key = OpenLocalMachineKeyWithRestore(registryPath, true))
                {
                    if (key != null)
                    {
                        key.SetAccessControl(acl);

                        // Recursively unlock all child subkeys (Parameters, Security, etc.)
                        try
                        {
                            string[] subKeyNames = key.GetSubKeyNames();
                            foreach (string subKeyName in subKeyNames)
                            {
                                string subPath = registryPath + "\\" + subKeyName;
                                try
                                {
                                    using (RegistryKey subKey = OpenLocalMachineKeyWithRestore(subPath, true))
                                    {
                                        if (subKey != null)
                                        {
                                            RegistrySecurity subAcl = new RegistrySecurity();
                                            subAcl.SetAccessRuleProtection(false, true);
                                            subAcl.AddAccessRule(new RegistryAccessRule(adminSid, RegistryRights.FullControl, AccessControlType.Allow));
                                            subAcl.AddAccessRule(new RegistryAccessRule(systemSid, RegistryRights.FullControl, AccessControlType.Allow));
                                            subAcl.AddAccessRule(new RegistryAccessRule(usersSid, RegistryRights.ReadKey, AccessControlType.Allow));
                                            subAcl.AddAccessRule(new RegistryAccessRule(appPkgSid, RegistryRights.ReadKey, AccessControlType.Allow));

                                            subKey.SetAccessControl(subAcl);
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        TakeOwnership(registryPath);
                        using (RegistryKey fallbackKey = Registry.LocalMachine.OpenSubKey(registryPath, RegistryKeyPermissionCheck.Default, RegistryRights.ChangePermissions))
                        {
                            if (fallbackKey != null)
                            {
                                fallbackKey.SetAccessControl(acl);
                            }
                        }
                    }
                }
                OperationLog.Info("Registry anahtari acildi: " + serviceName);
            }
            catch (Exception ex)
            {
                OperationLog.Error("Registry anahtari acilamadi: " + serviceName, ex);
            }
        }

        // K-2/K-3: kilitleme öncesi orijinal SDDL'yi CloseDefinitions altına yazar; UnlockRegistryKey oradan gerçek default'u geri yükler
        private void BackupRegistrySecurity(string serviceName, string registryPath)
        {
            try
            {
                string backupSubKey = @"SOFTWARE\WuLocker\CloseDefinitions\WuLockerSddlBackup\" + serviceName;
                using (RegistryKey existing = Registry.LocalMachine.OpenSubKey(backupSubKey))
                {
                    if (existing != null && existing.GetValue("Sddl") != null)
                    {
                        // Orijinal yedek zaten var; kilitli durumu tekrar yedekleme
                        return;
                    }
                }

                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath, RegistryKeyPermissionCheck.Default, RegistryRights.ReadKey))
                {
                    if (key == null) return;
                    RegistrySecurity original = key.GetAccessControl(AccessControlSections.All);
                    if (original.AreAccessRulesProtected)
                    {
                        // Anahtar zaten kilitli; kilitli halini orijinal olarak saklama
                        return;
                    }

                    byte[] sdBytes = original.GetSecurityDescriptorBinaryForm();
                    System.Security.AccessControl.CommonSecurityDescriptor sd =
                        new System.Security.AccessControl.CommonSecurityDescriptor(true, false, sdBytes, 0);
                    string sddl = sd.GetSddlForm(AccessControlSections.All);

                    using (RegistryKey backupKey = Registry.LocalMachine.CreateSubKey(backupSubKey))
                    {
                        if (backupKey != null)
                        {
                            backupKey.SetValue("Sddl", sddl, RegistryValueKind.String);
                            backupKey.SetValue("Path", registryPath, RegistryValueKind.String);
                        }
                    }
                }
                OperationLog.Info("SDDL yedeklendi: " + serviceName);
            }
            catch (Exception ex)
            {
                OperationLog.Error("SDDL yedeklenemedi: " + serviceName, ex);
            }
        }

        // K-3: SDDL yedeği mevcutsa gerçek default'u geri yükler ve true döner; yede yoksa false
        private bool TryRestoreFromSddlBackup(string serviceName)
        {
            try
            {
                string backupSubKey = @"SOFTWARE\WuLocker\CloseDefinitions\WuLockerSddlBackup\" + serviceName;
                using (RegistryKey backupKey = Registry.LocalMachine.OpenSubKey(backupSubKey))
                {
                    if (backupKey == null) return false;

                    string sddl = Convert.ToString(backupKey.GetValue("Sddl"));
                    string path = Convert.ToString(backupKey.GetValue("Path"));
                    if (string.IsNullOrEmpty(sddl) || string.IsNullOrEmpty(path))
                    {
                        OperationLog.Info("SDDL yedegi eksik, sablon ACL uygulanacak: " + serviceName);
                        return false;
                    }

                    RegistrySecurity acl = new RegistrySecurity();
                    acl.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.All);
                    // Orijinal SDDL geri gelse bile Administrators ve SYSTEM'in FullControl sahibi olması garanti edilmeli
                    SecurityIdentifier adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                    SecurityIdentifier systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                    acl.SetAccessRuleProtection(false, true);
                    acl.AddAccessRule(new RegistryAccessRule(adminSid, RegistryRights.FullControl, AccessControlType.Allow));
                    acl.AddAccessRule(new RegistryAccessRule(systemSid, RegistryRights.FullControl, AccessControlType.Allow));

                    bool applied = false;
                    using (RegistryKey key = OpenLocalMachineKeyWithRestore(path, true))
                    {
                        if (key != null)
                        {
                            key.SetAccessControl(acl);
                            applied = true;
                            OperationLog.Info("SDDL yedekten geri yüklendi: " + serviceName);
                        }
                    }

                    // Alt anahtarlara da aynı default kuralları uygula (recursive)
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path))
                    {
                        if (key != null)
                        {
                            foreach (string subKeyName in key.GetSubKeyNames())
                            {
                                try
                                {
                                    string subPath = path + "\\" + subKeyName;
                                    using (RegistryKey subKey = OpenLocalMachineKeyWithRestore(subPath, true))
                                    {
                                        if (subKey != null)
                                        {
                                            subKey.SetAccessControl(acl);
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    return applied;
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("SDDL geri yüklenemedi: " + serviceName, ex);
                return false;
            }
        }

        public void ManageScheduledTasks(bool enable)
        {
            if (enable)
            {
                LockScheduledTasks(false);
            }

            string action = enable ? "/Enable" : "/Disable";
            foreach (string task in config.TasksToManage)
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

            if (!enable)
            {
                LockScheduledTasks(true);
            }
        }

        public void SetServiceRecovery(string serviceName, bool disable)
        {
            if (serviceName == "WaaSMedicSvc") return;
            try
            {
                string args = disable
                    ? string.Format("failure \"{0}\" reset= 0 actions= \"\"", serviceName)
                    : string.Format("failure \"{0}\" reset= 86400 actions= restart/60000", serviceName);

                OperationLog.RunProcess("sc.exe", args, 3000);

                if (disable)
                {
                    try
                    {
                        string regPath = @"SYSTEM\CurrentControlSet\Services\" + serviceName;
                        using (RegistryKey key = OpenLocalMachineKeyWithRestore(regPath, true) ?? Registry.LocalMachine.OpenSubKey(regPath, true))
                        {
                            if (key != null)
                            {
                                TryDeleteRegistryValue(key, "FailureActions");
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Servis kurtarma ayari degistirilemedi: " + serviceName, ex);
            }
        }

        public void LockScheduledTasks(bool lockTasks)
        {
            string tasksDir = GetTasksDirectory();

            foreach (string task in config.TasksToManage)
            {
                try
                {
                    string filePath = Path.Combine(tasksDir, task);
                    if (!File.Exists(filePath)) continue;

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

        public void BlockUpdateProcesses(bool block)
        {
            string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            foreach (string proc in config.BlockProcesses)
            {
                string filePath = Path.Combine(sysDir, proc);
                if (!File.Exists(filePath)) continue;
                try
                {
                    if (block)
                    {
                        OperationLog.RunProcess("takeown.exe", "/F \"" + filePath + "\" /A", 5000);
                        OperationLog.RunProcess("icacls.exe", "\"" + filePath + "\" /grant *S-1-5-32-544:F /C", 5000);
                        OperationLog.RunProcess("icacls.exe", "\"" + filePath + "\" /deny *S-1-1-0:(X) /C", 5000);
                    }
                    else
                    {
                        OperationLog.RunProcess("takeown.exe", "/F \"" + filePath + "\" /A", 5000);
                        OperationLog.RunProcess("icacls.exe", "\"" + filePath + "\" /grant *S-1-5-32-544:F /C", 5000);
                        OperationLog.RunProcess("icacls.exe", "\"" + filePath + "\" /remove:d *S-1-1-0 /C", 5000);
                        RestoreFileDefaultAcl(filePath);
                        OperationLog.RunProcess("icacls.exe", "\"" + filePath + "\" /setowner \"NT SERVICE\\TrustedInstaller\" /C", 5000);
                    }
                }
                catch (Exception ex)
                {
                    OperationLog.Error("Update process dosya izni degistirilemedi: " + proc, ex);
                }
            }
        }

        public void CloseUpdateProcesses()
        {
            foreach (string procName in config.CloseProcesses)
            {
                try
                {
                    Process[] procs = Process.GetProcessesByName(procName);
                    if (procs != null)
                    {
                        foreach (Process p in procs)
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
                            finally
                            {
                                p.Dispose();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OperationLog.Error("Process listesi okunamadi: " + procName, ex);
                }
            }
        }

        public void SetRegNoTrayIcon(bool hide)
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

        public void SetServiceImagePath(bool nullify)
        {
            foreach (string svc in config.ImagePathServices)
            {
                if (IsServiceExcludedForStore(svc))
                {
                    continue;
                }

                try
                {
                    string regPath = @"SYSTEM\CurrentControlSet\Services\" + svc;
                    if (!nullify)
                    {
                        UnlockRegistryKey(svc);
                    }
                    using (RegistryKey key = OpenLocalMachineKeyWithRestore(regPath, true) ?? Registry.LocalMachine.OpenSubKey(regPath, true))
                    {
                        if (key != null)
                        {
                            if (nullify)
                            {
                                object orig = key.GetValue("ImagePath");
                                if (orig != null && orig.ToString().Length > 0)
                                {
                                    key.SetValue("ImagePath_Backup", orig.ToString(), RegistryValueKind.ExpandString);
                                    key.SetValue("ImagePath", "", RegistryValueKind.ExpandString);
                                }
                            }
                            else
                            {
                                object backup = key.GetValue("ImagePath_Backup");
                                if (backup != null && backup.ToString().Length > 0)
                                {
                                    key.SetValue("ImagePath", backup.ToString(), RegistryValueKind.ExpandString);
                                    TryDeleteRegistryValue(key, "ImagePath_Backup");
                                }
                                else
                                {
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

        public void PauseUpdatesUntil2050()
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

        public void ClearPauseUpdates()
        {
            try
            {
                string path = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(path))
                {
                    if (key != null)
                    {
                        TryDeleteRegistryValue(key, "PauseUpdatesStartTime");
                        TryDeleteRegistryValue(key, "PauseUpdatesExpiryTime");
                        TryDeleteRegistryValue(key, "PauseFeatureUpdatesStartTime");
                        TryDeleteRegistryValue(key, "PauseFeatureUpdatesEndTime");
                        TryDeleteRegistryValue(key, "PauseQualityUpdatesStartTime");
                        TryDeleteRegistryValue(key, "PauseQualityUpdatesEndTime");
                        TryDeleteRegistryValue(key, "FlightSettingsMaxPauseDays");
                        key.SetValue("PauseUXState", 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Windows Update pause ayarlari temizlenemedi.", ex);
            }
        }

        public void RestoreFileDefaultAcl(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;
                OperationLog.RunProcess("icacls.exe", "\"" + filePath + "\" /reset", 10000);
            }
            catch (Exception ex)
            {
                OperationLog.Error("Dosya ACL varsayilana dondurulemedi: " + filePath, ex);
            }
        }

        public void TrySetFileOwnerToSystem(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;
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

        public void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    OperationLog.Info("Dosya silindi: " + path);
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Dosya silinemedi: " + path, ex);
            }
        }

        public void TryDeleteDirectory(string path)
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

        public void TryMoveDirectory(string sourcePath, string targetPath)
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

        public void TryDeleteRegistryValue(RegistryKey key, string valueName)
        {
            try
            {
                if (key != null && key.GetValue(valueName) != null)
                {
                    key.DeleteValue(valueName, false);
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Registry degeri silinemedi: " + valueName, ex);
            }
        }

        public void TryDeleteRegistrySubTree(RegistryKey key, string subKeyName)
        {
            try
            {
                if (key != null)
                {
                    using (RegistryKey sub = key.OpenSubKey(subKeyName))
                    {
                        if (sub != null)
                        {
                            key.DeleteSubKeyTree(subKeyName, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Registry alt anahtari silinemedi: " + subKeyName, ex);
            }
        }

        public void PurgeFolderContents(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    foreach (string file in Directory.GetFiles(path))
                    {
                        TryDeleteFile(file);
                    }
                    foreach (string dir in Directory.GetDirectories(path))
                    {
                        TryDeleteDirectory(dir);
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("Klasor icerigi temizlenemedi: " + path, ex);
            }
        }

        public void ExecuteRepair(Action<string> onProgress = null, bool deepScan = false, Action<string> onOutputLine = null, Dictionary<string, string> localizationDict = null)
        {
            if (localizationDict == null)
            {
                string lang = AppConfig.ReadString(IniPath, "Settings", "Language", "Auto", 64);
                localizationDict = Program.LoadLocalization(lang);
            }

            Func<string, string, string> loc = delegate(string key, string fallback)
            {
                return LocalizationManager.Get(localizationDict, key, fallback);
            };

            Action<string> logStep = delegate(string msg)
            {
                OperationLog.Info("Repair Adimi: " + msg);
                if (onProgress != null)
                {
                    try { onProgress(msg); } catch { }
                }
            };

            // 1. Stop services
            logStep(loc("RepairStep_StopServices", "Güncelleştirme servisleri durduruluyor..."));
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
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(3));
                        }
                    }
                }
                catch (Exception ex)
                {
                    OperationLog.Error("Servis durdurulamadi: " + sName, ex);
                }
            }

            // If any service is still lingering or locking files, terminate it forcibly
            foreach (string sName in stopServices)
            {
                try
                {
                    using (ServiceController sc = new ServiceController(sName))
                    {
                        if (sc.Status != ServiceControllerStatus.Stopped)
                        {
                            OperationLog.RunProcess("taskkill.exe", "/f /fi \"SERVICES eq " + sName + "\"", 3000);
                        }
                    }
                }
                catch { }
            }

            // 2. Restore service registry permissions and recovery options
            logStep(loc("RepairStep_UnlockRegistry", "Kayıt defteri yetkileri ve kurtarma seçenekleri sıfırlanıyor..."));
            foreach (string sName in config.Services)
            {
                UnlockRegistryKey(sName);
                SetServiceRecovery(sName, false);
            }

            // 2.5. Remove NTFS execution restrictions on update processes
            logStep(loc("RepairStep_UnlockProcesses", "İşlem yürütme kısıtlamaları kaldırılıyor..."));
            BlockUpdateProcesses(false);

            // 2.8. Restore Service Control Manager default security descriptors (SDDL)
            logStep(loc("RepairStep_RestoreSddl", "Hizmet güvenlik tanımlayıcıları (SDDL) varsayılana sıfırlanıyor..."));
            OperationLog.RunProcess("sc.exe", "sdset wuauserv D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)", 3000, onOutputLine);
            OperationLog.RunProcess("sc.exe", "sdset bits D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)", 3000, onOutputLine);
            OperationLog.RunProcess("sc.exe", "sdset cryptsvc D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)", 3000, onOutputLine);
            OperationLog.RunProcess("sc.exe", "config wuauserv type= own", 3000, onOutputLine);
            OperationLog.RunProcess("sc.exe", "sidtype wuauserv unrestricted", 3000, onOutputLine);
            OperationLog.RunProcess("sc.exe", "config UsoSvc type= own", 3000, onOutputLine);
            OperationLog.RunProcess("sc.exe", "config bits type= own", 3000, onOutputLine);

            // 2.9. Ensure core dependency services (RpcSs, DcomLaunch, EventLog) are enabled
            logStep(loc("RepairStep_CheckDependencies", "Kritik bağımlılık servisleri (EventLog, RpcSs) kontrol ediliyor..."));
            string[] depServices = { "RpcSs", "DcomLaunch", "EventLog" };
            foreach (string dep in depServices)
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + dep, true))
                    {
                        if (key != null)
                        {
                            object startVal = key.GetValue("Start");
                            if (startVal != null && Convert.ToInt32(startVal) == 4)
                            {
                                key.SetValue("Start", 2, RegistryValueKind.DWord);
                            }
                        }
                    }
                }
                catch { }
            }

            // 3. Clear BITS queue
            logStep(loc("RepairStep_ClearBits", "BITS indirme kuyruğu temizleniyor..."));
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

            // 4. Rename or purge SoftwareDistribution cache folder
            logStep(loc("RepairStep_ClearCache", "SoftwareDistribution güncelleme önbelleği sıfırlanıyor..."));
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
                    try
                    {
                        Directory.Move(sdPath, sdPathOld);
                    }
                    catch
                    {
                        PurgeFolderContents(Path.Combine(sdPath, "DataStore"));
                        PurgeFolderContents(Path.Combine(sdPath, "Download"));
                        PurgeFolderContents(Path.Combine(sdPath, "PostRebootEventCache"));
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("SoftwareDistribution tasinamadi.", ex);
            }

            // 5. Rename or purge Catroot2 cache folder
            logStep(loc("RepairStep_ClearCatroot", "Catroot2 kriptografik önbelleği sıfırlanıyor..."));
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
                    try
                    {
                        Directory.Move(catrootPath, catrootPathOld);
                    }
                    catch
                    {
                        PurgeFolderContents(catrootPath);
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("catroot2 tasinamadi.", ex);
            }

            // 6. Clear Delivery Optimization cache
            logStep(loc("RepairStep_ClearDoCache", "Teslimat İyileştirme önbelleği temizleniyor..."));
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
            logStep(loc("RepairStep_ClearGpo", "Yerel Grup İlkesi dosyaları temizleniyor..."));
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

            // 7. Reset Winsock, TCP/IP, DNS, and HTTP Proxy
            logStep(loc("RepairStep_ResetNetwork", "Ağ protokolleri, DNS ve WinHTTP proxy sıfırlanıyor..."));
            OperationLog.RunProcess("netsh.exe", "winsock reset", 5000, onOutputLine);
            OperationLog.RunProcess("netsh.exe", "int ip reset", 5000, onOutputLine);
            OperationLog.RunProcess("ipconfig.exe", "/flushdns", 5000, onOutputLine);
            OperationLog.RunProcess("netsh.exe", "winhttp reset proxy", 5000, onOutputLine);

            // 8. Re-register core Windows Update and Cryptographic DLLs
            logStep(loc("RepairStep_RegisterDlls", "Güncelleme ve güvenlik DLL kütüphaneleri kaydediliyor..."));
            string[] wudlls = {
                "wuaueng.dll", "wuapi.dll", "wups.dll", "wups2.dll", "wucltux.dll", "atl.dll",
                "msxml3.dll", "softpub.dll", "wintrust.dll", "initpki.dll", "dssenh.dll", "rsaenh.dll", "urlmon.dll"
            };
            foreach (string dll in wudlls)
            {
                OperationLog.RunProcess("regsvr32.exe", "/s " + dll, 5000, onOutputLine);
            }

            // 8.5. Check and repair WMI Repository
            logStep(loc("RepairStep_SalvageWmi", "WMI veri havuzu denetleniyor ve onarılıyor..."));
            OperationLog.RunProcess("winmgmt.exe", "/salvagerepository", 10000, onOutputLine);

            // 9 & 10. Deep Scan (SFC and DISM) if requested
            if (deepScan)
            {
                logStep(loc("RepairStep_Sfc", "Sistem Dosyası Denetleyicisi (SFC /scannow) çalıştırılıyor..."));
                OperationLog.RunProcess("sfc.exe", "/scannow", 0, onOutputLine);

                logStep(loc("RepairStep_Dism", "DISM Bileşen Deposu onarımı çalıştırılıyor..."));
                OperationLog.RunProcess("dism.exe", "/online /cleanup-image /restorehealth", 0, onOutputLine);
            }
            else
            {
                logStep(loc("RepairStep_SkipDeep", "Hızlı onarım modu: SFC/DISM atlandı."));
            }

            // 10.5. Repair Microsoft Store via PowerShell
            logStep(loc("RepairStep_RepairStore", "Microsoft Store uygulaması onarılıyor..."));
            OperationLog.RunProcess("powershell.exe", "-ExecutionPolicy Bypass -Command \"Get-AppXPackage -AllUsers -Name Microsoft.WindowsStore | Foreach {Add-AppxPackage -DisableDevelopmentMode -Register \\\"$($_.InstallLocation)\\AppXManifest.xml\\\"}\"", 30000, onOutputLine);

            // 11. Remove registry policy block keys and clean WSUS hijacks
            logStep(loc("RepairStep_ClearPolicies", "Engelleme ilkeleri ve sahte WSUS yönlendirmeleri temizleniyor..."));
            ApplyRegistryPolicies(false);
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", true))
                {
                    if (key != null)
                    {
                        TryDeleteRegistryValue(key, "WUServer");
                        TryDeleteRegistryValue(key, "WUStatusServer");
                        TryDeleteRegistryValue(key, "UpdateServiceUrlAlternate");
                    }
                }
            }
            catch (Exception ex)
            {
                OperationLog.Error("WSUS yonlendirme anahtarlari temizlenemedi.", ex);
            }

            // 11.5. Clean pending update and orchestrator registry states
            logStep(loc("RepairStep_ClearPending", "Bekleyen güncelleştirme kayıt defteri durumları temizleniyor..."));
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
            logStep(loc("RepairStep_StartServices", "Servis ayarları yapılandırılıyor ve servisler başlatılıyor..."));
            SetRegNoTrayIcon(false);
            SetServiceImagePath(false);
            ManageScheduledTasks(true);

            foreach (string sName in config.Services)
            {
                RestoreAndEnableService(sName);
            }

            // 13. Broadcast settings change notifications and refresh policy cache
            logStep(loc("RepairStep_BroadcastNotify", "Sistem ilke bildirimleri yayınlanıyor..."));
            RefreshSystemPolicyCache();

            logStep(loc("RepairStep_Complete", "Onarım başarıyla tamamlandı!"));
        }
    }
}
