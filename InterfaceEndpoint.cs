using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;

namespace InterfaceTester
{
    internal sealed class InterfaceEndpoint
    {
        public int Number { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string P12Path { get; set; }
        public string P12Password { get; set; }
        public bool Enabled { get; set; }
        public bool ProbeTls { get; set; }
        public bool ProbeHttpGet { get; set; }
        public bool ProbeWsdl { get; set; }
        public bool ProbeSoap { get; set; }
        public string SoapAction { get; set; }
        public string SoapEnvelopePath { get; set; }
        public string SoapEnvelopeXml { get; set; }
        public string ContentType { get; set; }
        public bool? AcceptUntrustedServerCertificate { get; set; }

        public Uri ParsedUrl
        {
            get { return new Uri(Url, UriKind.Absolute); }
        }

        public bool AcceptUntrusted
        {
            get
            {
                if (AcceptUntrustedServerCertificate.HasValue)
                {
                    return AcceptUntrustedServerCertificate.Value;
                }

                return AppSettings.AcceptUntrustedServerCertificates;
            }
        }

        public static List<InterfaceEndpoint> LoadFromAppConfig()
        {
            List<InterfaceEndpoint> endpoints = new List<InterfaceEndpoint>();

            for (int number = 1; number <= 99; number++)
            {
                string prefix = "Interface" + number + ".";

                if (!AppSettings.HasAppSetting(prefix + "Name"))
                {
                    break;
                }

                endpoints.Add(Parse(number, prefix));
            }

            if (endpoints.Count == 0)
            {
                throw new ConfigurationErrorsException(
                    "No interfaces were found in App.config. " +
                    "Add Interface1.Name, Interface1.Url, Interface1.P12Path, ...");
            }

            return endpoints;
        }

        private static InterfaceEndpoint Parse(int number, string prefix)
        {
            InterfaceEndpoint endpoint = new InterfaceEndpoint();

            endpoint.Number = number;
            endpoint.Name = AppSettings.GetRequiredAppSetting(prefix + "Name");
            endpoint.Url = AppSettings.GetRequiredAppSetting(prefix + "Url");
            endpoint.P12Path = ResolvePath(
                AppSettings.GetRequiredAppSetting(prefix + "P12Path"));
            endpoint.P12Password = AppSettings.GetOptionalAppSetting(
                prefix + "P12Password",
                "");
            endpoint.Enabled = AppSettings.GetOptionalBoolAppSetting(
                prefix + "Enabled",
                true);
            endpoint.SoapAction = AppSettings.GetAppSettingAllowEmpty(
                prefix + "SoapAction",
                "http://tempuri.org/Test");
            endpoint.ContentType = AppSettings.GetOptionalAppSetting(
                prefix + "ContentType",
                "text/xml; charset=utf-8");

            string soapEnvelopePath = AppSettings.GetOptionalAppSetting(
                prefix + "SoapEnvelopePath",
                "");

            if (!String.IsNullOrWhiteSpace(soapEnvelopePath))
            {
                endpoint.SoapEnvelopePath = ResolvePath(soapEnvelopePath);
            }

            if (AppSettings.HasAppSetting(prefix + "AcceptUntrustedServerCertificate"))
            {
                endpoint.AcceptUntrustedServerCertificate =
                    AppSettings.GetOptionalBoolAppSetting(
                        prefix + "AcceptUntrustedServerCertificate",
                        false);
            }

            ApplyProbes(
                endpoint,
                AppSettings.GetOptionalAppSetting(prefix + "Probes", ""));

            ValidateUrl(endpoint);

            return endpoint;
        }

        private static void ApplyProbes(InterfaceEndpoint endpoint, string probes)
        {
            if (String.IsNullOrWhiteSpace(probes))
            {
                endpoint.ProbeTls = true;

                if (endpoint.HasSoapPayload())
                {
                    endpoint.ProbeSoap = true;
                }
                else
                {
                    endpoint.ProbeHttpGet = true;
                    endpoint.ProbeWsdl = true;
                }

                return;
            }

            string[] parts = probes.Split(
                new[] { ',', ';', ' ' },
                StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < parts.Length; i++)
            {
                string probe = parts[i].Trim().ToLowerInvariant();

                if (probe == "tls")
                {
                    endpoint.ProbeTls = true;
                }
                else if (probe == "http" || probe == "get")
                {
                    endpoint.ProbeHttpGet = true;
                }
                else if (probe == "wsdl")
                {
                    endpoint.ProbeWsdl = true;
                }
                else if (probe == "soap")
                {
                    endpoint.ProbeSoap = true;
                }
                else
                {
                    throw new ConfigurationErrorsException(
                        "Unknown probe '" + parts[i] + "' on Interface" +
                        endpoint.Number + " (" + endpoint.Name +
                        "). Use tls, http, wsdl, soap.");
                }
            }
        }

        public bool HasSoapPayload()
        {
            return !String.IsNullOrWhiteSpace(SoapEnvelopeXml) ||
                   !String.IsNullOrWhiteSpace(SoapEnvelopePath);
        }

        public string LoadSoapEnvelope()
        {
            if (!String.IsNullOrWhiteSpace(SoapEnvelopeXml))
            {
                return SoapEnvelopeXml;
            }

            if (String.IsNullOrWhiteSpace(SoapEnvelopePath))
            {
                throw new InvalidOperationException(
                    "Interface '" + Name +
                    "' is configured for SOAP but has no envelope.");
            }

            if (!File.Exists(SoapEnvelopePath))
            {
                throw new FileNotFoundException(
                    "SOAP envelope file was not found for interface '" +
                    Name + "'.",
                    SoapEnvelopePath);
            }

            return File.ReadAllText(SoapEnvelopePath);
        }

        private static void ValidateUrl(InterfaceEndpoint endpoint)
        {
            Uri uri;

            if (!Uri.TryCreate(endpoint.Url, UriKind.Absolute, out uri) ||
                uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ConfigurationErrorsException(
                    "Interface" + endpoint.Number + " (" + endpoint.Name +
                    ") Url must be an https URL. Current value: " +
                    endpoint.Url);
            }
        }

        private static string ResolvePath(string path)
        {
            path = path.Replace('\\', Path.DirectorySeparatorChar)
                       .Replace('/', Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            if (String.IsNullOrEmpty(baseDirectory))
            {
                baseDirectory = Directory.GetCurrentDirectory();
            }

            return Path.GetFullPath(Path.Combine(baseDirectory, path));
        }
    }
}
