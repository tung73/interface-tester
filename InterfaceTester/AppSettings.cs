using System;
using System.Configuration;

namespace InterfaceTester
{
    internal static class AppSettings
    {
        public static string InterfacesFile
        {
            get { return GetOptionalAppSetting("InterfacesFile", "Interfaces.xml"); }
        }

        public static int ConnectionTimeoutSeconds
        {
            get { return GetOptionalIntAppSetting("ConnectionTimeoutSeconds", 30, 1, 600); }
        }

        public static int TlsHandshakeTimeoutSeconds
        {
            get { return GetOptionalIntAppSetting("TlsHandshakeTimeoutSeconds", 30, 1, 600); }
        }

        public static int HttpTimeoutSeconds
        {
            get { return GetOptionalIntAppSetting("HttpTimeoutSeconds", 30, 1, 600); }
        }

        public static int ResponsePreviewChars
        {
            get { return GetOptionalIntAppSetting("ResponsePreviewChars", 2000, 100, 20000); }
        }

        public static bool CheckCertificateRevocation
        {
            get { return GetOptionalBoolAppSetting("CheckCertificateRevocation", false); }
        }

        public static bool AcceptUntrustedServerCertificates
        {
            get { return GetOptionalBoolAppSetting("AcceptUntrustedServerCertificates", false); }
        }

        public static string GetOptionalAppSetting(string key, string defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];

            if (String.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            return value.Trim();
        }

        public static int GetOptionalIntAppSetting(
            string key,
            int defaultValue,
            int minValue,
            int maxValue)
        {
            string value = ConfigurationManager.AppSettings[key];

            if (String.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            int parsedValue;

            if (!Int32.TryParse(value, out parsedValue) ||
                parsedValue < minValue ||
                parsedValue > maxValue)
            {
                throw new ConfigurationErrorsException(
                    "App.config setting '" + key +
                    "' must be a number between " + minValue +
                    " and " + maxValue + ". Current value: " + value);
            }

            return parsedValue;
        }

        public static bool GetOptionalBoolAppSetting(string key, bool defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];

            if (String.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            bool parsedValue;

            if (!Boolean.TryParse(value, out parsedValue))
            {
                throw new ConfigurationErrorsException(
                    "App.config setting '" + key +
                    "' must be true or false. Current value: " + value);
            }

            return parsedValue;
        }
    }
}
