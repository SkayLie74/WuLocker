using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WuLocker
{
    public static class LocalizationManager
    {
        public static readonly string[] SupportedLanguages = new string[] { "TR", "EN", "DE", "ES", "FR", "IT", "JA", "KO", "PT", "RU", "ZH" };

        public static Dictionary<string, string> Load(string baseDir, string langCode)
        {
            Dictionary<string, string> dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string normalized = NormalizeLanguage(langCode);
                string localesDir = Path.Combine(baseDir, "locales");
                string jsonPath = Path.Combine(localesDir, normalized + ".json");

                if (!File.Exists(jsonPath))
                {
                    jsonPath = Path.Combine(localesDir, "en.json");
                }

                if (!File.Exists(jsonPath))
                {
                    OperationLog.Info("Locale dosyasi bulunamadi: " + jsonPath);
                    return dict;
                }

                string json = File.ReadAllText(jsonPath, Encoding.UTF8);
                dict = ParseJsonObject(json, jsonPath);
            }
            catch (Exception ex)
            {
                OperationLog.Error("Yerellestirme yuklenemedi.", ex);
            }

            return dict;
        }

        public static string NormalizeLanguage(string langCode)
        {
            string normalized = langCode;
            if (string.IsNullOrEmpty(normalized))
            {
                normalized = "Auto";
            }

            if (normalized.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                normalized = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            }

            normalized = normalized.Trim().ToLowerInvariant();

            bool supported = false;
            int i = 0;
            while (i < SupportedLanguages.Length)
            {
                if (SupportedLanguages[i].Equals(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    supported = true;
                    break;
                }
                i++;
            }

            if (!supported)
            {
                normalized = "en";
            }

            return normalized;
        }

        public static string Get(Dictionary<string, string> dict, string key, string fallback)
        {
            if (dict != null && dict.ContainsKey(key))
            {
                return dict[key];
            }

            return fallback;
        }

        public static Dictionary<string, string> ParseJsonObject(string json, string sourceName)
        {
            Dictionary<string, string> dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> raw = serializer.Deserialize<Dictionary<string, object>>(json);

            if (raw == null)
            {
                return dict;
            }

            foreach (KeyValuePair<string, object> pair in raw)
            {
                if (pair.Value != null)
                {
                    dict[pair.Key] = pair.Value.ToString();
                }
                else
                {
                    dict[pair.Key] = "";
                }
            }

            return dict;
        }

        public static bool ValidateLocaleFiles(string localesDir, out string message)
        {
            StringBuilder errors = new StringBuilder();

            if (!Directory.Exists(localesDir))
            {
                message = "Locale dizini bulunamadi: " + localesDir;
                return false;
            }

            string[] files = Directory.GetFiles(localesDir, "*.json");
            int i = 0;
            while (i < files.Length)
            {
                try
                {
                    string json = File.ReadAllText(files[i], Encoding.UTF8);
                    ParseJsonObject(json, files[i]);
                }
                catch (Exception ex)
                {
                    errors.AppendLine(Path.GetFileName(files[i]) + ": " + ex.Message);
                }
                i++;
            }

            if (errors.Length > 0)
            {
                message = errors.ToString();
                return false;
            }

            message = "Tum locale dosyalari gecerli.";
            return true;
        }
    }
}
