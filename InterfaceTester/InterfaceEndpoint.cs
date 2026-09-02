using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace InterfaceTester
{
    internal sealed class InterfaceEndpoint
    {
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

        public static List<InterfaceEndpoint> LoadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Interfaces file was not found.",
                    path);
            }

            XmlDocument document = new XmlDocument();
            document.Load(path);

            XmlNodeList nodes = document.SelectNodes("/interfaces/interface");

            if (nodes == null || nodes.Count == 0)
            {
                throw new InvalidOperationException(
                    "No <interface> entries were found in " + path + ".");
            }

            List<InterfaceEndpoint> endpoints = new List<InterfaceEndpoint>();

            foreach (XmlNode node in nodes)
            {
                endpoints.Add(Parse(node, path));
            }

            return endpoints;
        }

        private static InterfaceEndpoint Parse(XmlNode node, string configPath)
        {
            InterfaceEndpoint endpoint = new InterfaceEndpoint();

            endpoint.Name = GetRequiredAttribute(node, "name");
            endpoint.Url = GetRequiredAttribute(node, "url");
            endpoint.P12Path = ResolvePath(
                GetRequiredAttribute(node, "p12Path"),
                configPath);
            endpoint.P12Password = GetOptionalAttribute(node, "p12Password", "");
            endpoint.Enabled = ParseBoolean(
                GetOptionalAttribute(node, "enabled", "true"),
                "enabled");
            endpoint.SoapAction = GetOptionalAttribute(
                node,
                "soapAction",
                "http://tempuri.org/Test");
            endpoint.ContentType = GetOptionalAttribute(
                node,
                "contentType",
                "text/xml; charset=utf-8");

            string soapEnvelopePath = GetOptionalAttribute(node, "soapEnvelopePath", "");

            if (!String.IsNullOrWhiteSpace(soapEnvelopePath))
            {
                endpoint.SoapEnvelopePath = ResolvePath(soapEnvelopePath, configPath);
            }

            string acceptUntrusted = GetOptionalAttribute(
                node,
                "acceptUntrustedServerCertificate",
                "");

            if (!String.IsNullOrWhiteSpace(acceptUntrusted))
            {
                endpoint.AcceptUntrustedServerCertificate = ParseBoolean(
                    acceptUntrusted,
                    "acceptUntrustedServerCertificate");
            }

            XmlNode soapNode = node.SelectSingleNode("soapEnvelope");

            if (soapNode != null && !String.IsNullOrWhiteSpace(soapNode.InnerText))
            {
                endpoint.SoapEnvelopeXml = soapNode.InnerText.Trim();
            }

            ApplyProbes(endpoint, GetOptionalAttribute(node, "probes", ""));

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
                    throw new InvalidOperationException(
                        "Unknown probe '" + parts[i] + "' on interface '" +
                        endpoint.Name + "'. Use tls, http, wsdl, soap.");
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
                throw new InvalidOperationException(
                    "Interface '" + endpoint.Name +
                    "' url must be an https URL. Current value: " +
                    endpoint.Url);
            }
        }

        private static string GetRequiredAttribute(XmlNode node, string name)
        {
            string value = GetOptionalAttribute(node, name, "");

            if (String.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    "Interface is missing required attribute '" + name + "'.");
            }

            return value;
        }

        private static string GetOptionalAttribute(
            XmlNode node,
            string name,
            string defaultValue)
        {
            XmlAttribute attribute = node.Attributes != null
                ? node.Attributes[name]
                : null;

            if (attribute == null || String.IsNullOrWhiteSpace(attribute.Value))
            {
                return defaultValue;
            }

            return attribute.Value.Trim();
        }

        private static bool ParseBoolean(string value, string attributeName)
        {
            bool parsedValue;

            if (!Boolean.TryParse(value, out parsedValue))
            {
                throw new InvalidOperationException(
                    "Attribute '" + attributeName +
                    "' must be true or false. Current value: " + value);
            }

            return parsedValue;
        }

        private static string ResolvePath(string path, string configPath)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            string configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath));

            if (String.IsNullOrEmpty(configDirectory))
            {
                configDirectory = AppDomain.CurrentDomain.BaseDirectory;
            }

            return Path.GetFullPath(Path.Combine(configDirectory, path));
        }
    }
}
