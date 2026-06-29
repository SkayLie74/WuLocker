using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WuLocker
{
    public class ProcessRunResult
    {
        public string FileName = "";
        public string Arguments = "";
        public int ExitCode = -1;
        public bool TimedOut = false;
        public string Output = "";
        public string Error = "";
        public Exception Exception = null;

        public bool Success
        {
            get
            {
                if (Exception != null)
                {
                    return false;
                }
                if (TimedOut)
                {
                    return false;
                }
                if (ExitCode != 0)
                {
                    return false;
                }
                return true;
            }
        }
    }

    public static class OperationLog
    {
        private static readonly object SyncRoot = new object();
        private static string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WuLocker.log");
        public static bool Silent = false;

        public static string LogPath
        {
            get
            {
                return logPath;
            }
        }

        public static void Configure(bool silent)
        {
            Silent = silent;
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", message, ex);
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                StringBuilder line = new StringBuilder();
                line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                line.Append(" [");
                line.Append(level);
                line.Append("] ");
                line.Append(message);

                if (ex != null)
                {
                    line.Append(" | ");
                    line.Append(ex.GetType().Name);
                    line.Append(": ");
                    line.Append(ex.Message);
                }

                lock (SyncRoot)
                {
                    File.AppendAllText(logPath, line.ToString() + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        public static ProcessRunResult RunProcess(string fileName, string arguments, int timeoutMs)
        {
            ProcessRunResult result = new ProcessRunResult();
            result.FileName = fileName;
            result.Arguments = arguments;

            try
            {
                Info("Komut basladi: " + fileName + " " + arguments);

                ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                StringBuilder output = new StringBuilder();
                StringBuilder error = new StringBuilder();

                using (Process process = new Process())
                {
                    process.StartInfo = psi;
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                        {
                            output.AppendLine(e.Data);
                        }
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                        {
                            error.AppendLine(e.Data);
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    bool exited = false;
                    if (timeoutMs <= 0)
                    {
                        process.WaitForExit();
                        exited = true;
                    }
                    else
                    {
                        exited = process.WaitForExit(timeoutMs);
                    }

                    if (!exited)
                    {
                        result.TimedOut = true;
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }
                    }
                    else
                    {
                        process.WaitForExit();
                        result.ExitCode = process.ExitCode;
                    }
                }

                result.Output = output.ToString();
                result.Error = error.ToString();

                if (result.Success)
                {
                    Info("Komut tamamlandi: " + fileName + " cikis=" + result.ExitCode.ToString());
                }
                else
                {
                    Info("Komut hata veya zaman asimi ile bitti: " + fileName + " cikis=" + result.ExitCode.ToString());
                    if (result.Error.Length > 0)
                    {
                        Info("Komut stderr: " + result.Error.Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                result.Exception = ex;
                Error("Komut calistirilamadi: " + fileName + " " + arguments, ex);
            }

            return result;
        }
    }
}
