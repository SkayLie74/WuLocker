using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WuLocker
{
    public class AppConfig
    {
        public bool LockServicesOnBlock = true;
        public bool SetTaskScheduler = true;
        public bool SetRegNoTrayIcon = true;
        public bool SetImagePath = true;
        public bool ProcessBlockOptions = true;
        public bool ProcessCloseOptions = true;
        public bool PauseUpdatesOnEnable = false;

        public string[] Services = DefaultServices();
        public string[] TasksToManage = DefaultTasks();
        public string[] BlockProcesses = DefaultBlockProcesses();
        public string[] CloseProcesses = DefaultCloseProcesses();
        public string[] ImagePathServices = DefaultImagePathServices();

        [DllImport("kernel32.dll", EntryPoint = "GetPrivateProfileStringW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetPrivateProfileString(
            string section, string key, string def, StringBuilder retVal, uint size, string filePath);

        public static AppConfig Load(string iniPath)
        {
            AppConfig config = new AppConfig();

            if (string.IsNullOrEmpty(iniPath))
            {
                return config;
            }

            if (!File.Exists(iniPath))
            {
                OperationLog.Info("INI bulunamadi, varsayilan ayarlar kullaniliyor: " + iniPath);
                return config;
            }

            config.LockServicesOnBlock = ReadBool(iniPath, "Settings", "LockServicesOnBlock", config.LockServicesOnBlock);
            config.SetTaskScheduler = ReadBool(iniPath, "Settings", "SetTaskScheduler", config.SetTaskScheduler);
            config.SetRegNoTrayIcon = ReadBool(iniPath, "Settings", "SetRegNoTrayIcon", config.SetRegNoTrayIcon);
            config.SetImagePath = ReadBool(iniPath, "Settings", "SetImagePath", config.SetImagePath);
            config.ProcessBlockOptions = ReadBool(iniPath, "Settings", "ProcessBlockOptions", config.ProcessBlockOptions);
            config.ProcessCloseOptions = ReadBool(iniPath, "Settings", "ProcessCloseOptions", config.ProcessCloseOptions);
            config.PauseUpdatesOnEnable = ReadBool(iniPath, "Settings", "PauseUpdatesOnEnable", config.PauseUpdatesOnEnable);

            config.Services = ReadList(iniPath, "Services", "List", DefaultServices());
            config.TasksToManage = ReadList(iniPath, "Tasks", "List", DefaultTasks());
            config.BlockProcesses = ReadList(iniPath, "BlockProcesses", "List", DefaultBlockProcesses());
            config.CloseProcesses = ReadList(iniPath, "CloseProcesses", "List", DefaultCloseProcesses());
            config.ImagePathServices = ReadList(iniPath, "ImagePathServices", "List", DefaultImagePathServices());

            return config;
        }

        public static string ReadString(string iniPath, string section, string key, string defaultValue, int bufferSize)
        {
            try
            {
                if (!File.Exists(iniPath))
                {
                    return defaultValue;
                }

                StringBuilder temp = new StringBuilder(bufferSize);
                GetPrivateProfileString(section, key, defaultValue, temp, (uint)bufferSize, iniPath);
                return temp.ToString();
            }
            catch (Exception ex)
            {
                OperationLog.Error("INI okunamadi: " + section + "/" + key, ex);
                return defaultValue;
            }
        }

        public static bool ReadBool(string iniPath, string section, string key, bool defaultValue)
        {
            string fallback = "0";
            if (defaultValue)
            {
                fallback = "1";
            }

            string raw = ReadString(iniPath, section, key, fallback, 64);
            if (string.IsNullOrEmpty(raw))
            {
                return defaultValue;
            }

            raw = raw.Trim();
            if (raw.Equals("1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (raw.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (raw.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (raw.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (raw.Equals("0", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (raw.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (raw.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (raw.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            OperationLog.Info("INI boolean degeri gecersiz, varsayilan kullanildi: " + section + "/" + key + "=" + raw);
            return defaultValue;
        }

        public static string[] ReadList(string iniPath, string section, string key, string[] defaultValues)
        {
            try
            {
                string defaultStr = string.Join(",", defaultValues);
                string val = ReadString(iniPath, section, key, defaultStr, 8192);
                if (string.IsNullOrEmpty(val))
                {
                    return Clone(defaultValues);
                }

                string[] rawItems = val.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                string[] cleanItems = new string[rawItems.Length];
                int count = 0;

                int i = 0;
                while (i < rawItems.Length)
                {
                    string item = rawItems[i].Trim();
                    if (item.Length > 0)
                    {
                        cleanItems[count] = item;
                        count++;
                    }
                    i++;
                }

                if (count == 0)
                {
                    return Clone(defaultValues);
                }

                string[] result = new string[count];
                Array.Copy(cleanItems, result, count);
                return result;
            }
            catch (Exception ex)
            {
                OperationLog.Error("INI liste okunamadi: " + section + "/" + key, ex);
                return Clone(defaultValues);
            }
        }

        public static string[] Clone(string[] source)
        {
            if (source == null)
            {
                return new string[0];
            }

            string[] result = new string[source.Length];
            Array.Copy(source, result, source.Length);
            return result;
        }

        public static string[] DefaultServices()
        {
            return new string[] { "wuauserv", "UsoSvc", "WaaSMedicSvc", "dosvc", "BITS", "InstallService" };
        }

        public static string[] DefaultTasks()
        {
            return new string[]
            {
                @"Microsoft\Windows\WindowsUpdate\Scheduled Start",
                @"Microsoft\Windows\WindowsUpdate\Refresh Group Policy Cache",
                @"Microsoft\Windows\UpdateOrchestrator\Schedule Maintenance Work",
                @"Microsoft\Windows\UpdateOrchestrator\Schedule Wake To Work",
                @"Microsoft\Windows\UpdateOrchestrator\Schedule Work",
                @"Microsoft\Windows\UpdateOrchestrator\Schedule Scan",
                @"Microsoft\Windows\UpdateOrchestrator\Schedule Scan Static Task",
                @"Microsoft\Windows\UpdateOrchestrator\USO_UxBroker",
                @"Microsoft\Windows\UpdateOrchestrator\Report policies",
                @"Microsoft\Windows\UpdateOrchestrator\UpdateModelTask",
                @"Microsoft\Windows\UpdateOrchestrator\Start Oobe Expedite Work",
                @"Microsoft\Windows\UpdateOrchestrator\StartOobeAppsScanAfterUpdate",
                @"Microsoft\Windows\UpdateOrchestrator\StartOobeAppsScan_LicenseAccepted",
                @"Microsoft\Windows\UpdateOrchestrator\StartOobeAppsScan_OobeAppReady",
                @"Microsoft\Windows\WaaSMedic\PerformRemediation"
            };
        }

        public static string[] DefaultBlockProcesses()
        {
            return new string[]
            {
                "WaaSMedic.exe", "WaasMedicAgent.exe", "Windows10Upgrade.exe",
                "Windows10UpgraderApp.exe", "UpdateAssistant.exe", "UsoClient.exe",
                "remsh.exe", "EOSnotify.exe", "SihClient.exe", "InstallAgent.exe",
                "MusNotification.exe", "MusNotificationUx.exe", "MoNotificationUx.exe"
            };
        }

        public static string[] DefaultCloseProcesses()
        {
            return new string[] { "MusNotifyIcon", "MusNotification", "MusNotificationUx", "MoNotificationUx", "MoUsoCoreWorker" };
        }

        public static string[] DefaultImagePathServices()
        {
            return new string[] { "wuauserv", "dosvc", "WaaSMedicSvc", "UsoSvc" };
        }
    }
}
