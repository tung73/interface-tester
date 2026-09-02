using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace InterfaceTester
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            int exitCode = 1;

            try
            {
                exitCode = MainAsync(args).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine("FATAL ERROR");
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
                exitCode = 1;
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");

            if (!Console.IsInputRedirected)
            {
                Console.ReadKey(true);
            }

            return exitCode;
        }

        private static async Task<int> MainAsync(string[] args)
        {
            ConfigureServicePointManager();

            Console.WriteLine("============================================================");
            Console.WriteLine("Universal Mutual TLS / SOAP Connection Tester");
            Console.WriteLine(".NET Framework 4.8");
            Console.WriteLine("============================================================");
            Console.WriteLine(
                "Certificate revocation check : " +
                (AppSettings.CheckCertificateRevocation ? "ENABLED" : "DISABLED"));
            Console.WriteLine(
                "Accept untrusted server certs: " +
                AppSettings.AcceptUntrustedServerCertificates);
            Console.WriteLine(
                "Timeouts (TCP/TLS/HTTP)      : " +
                AppSettings.ConnectionTimeoutSeconds + " / " +
                AppSettings.TlsHandshakeTimeoutSeconds + " / " +
                AppSettings.HttpTimeoutSeconds + " seconds");

            string interfacesPath = ResolveInterfacesFile();
            Console.WriteLine("Interfaces file              : " + interfacesPath);
            Console.WriteLine();

            List<InterfaceEndpoint> allEndpoints =
                InterfaceEndpoint.LoadFromFile(interfacesPath);

            if (IsListRequest(args))
            {
                PrintInterfaceList(allEndpoints);
                return 0;
            }

            List<InterfaceEndpoint> endpoints = FilterEndpoints(allEndpoints, args);

            if (endpoints.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No enabled interfaces matched the request.");
                Console.ResetColor();
                return 1;
            }

            List<InterfaceResult> results = new List<InterfaceResult>();

            for (int i = 0; i < endpoints.Count; i++)
            {
                InterfaceResult result = await TestInterfaceAsync(endpoints[i]);
                results.Add(result);
            }

            PrintSummary(results);

            for (int i = 0; i < results.Count; i++)
            {
                if (!results[i].Passed)
                {
                    return 1;
                }
            }

            return 0;
        }

        private static async Task<InterfaceResult> TestInterfaceAsync(
            InterfaceEndpoint endpoint)
        {
            InterfaceResult result = new InterfaceResult(endpoint);

            Console.WriteLine();
            Console.WriteLine("============================================================");
            Console.WriteLine("INTERFACE: " + endpoint.Name);
            Console.WriteLine("============================================================");
            Console.WriteLine("URL      : " + endpoint.Url);
            Console.WriteLine("Host     : " + endpoint.ParsedUrl.Host);
            Console.WriteLine("Port     : " + endpoint.ParsedUrl.Port);
            Console.WriteLine("Path     : " + endpoint.ParsedUrl.PathAndQuery);
            Console.WriteLine("P12 path : " + endpoint.P12Path);
            Console.WriteLine(
                "Probes   : " + DescribeProbes(endpoint));
            Console.WriteLine(
                "Untrusted server certs: " +
                (endpoint.AcceptUntrusted ? "accepted" : "rejected"));

            X509Certificate2 clientCertificate;

            try
            {
                clientCertificate = CertificateLoader.LoadClientCertificate(
                    endpoint.P12Path,
                    endpoint.P12Password);

                CertificateLoader.PrintClientCertificateInformation(clientCertificate);
                result.CertificateLoaded = true;
            }
            catch (Exception ex)
            {
                result.CertificateLoaded = false;
                result.CertificateError = ex.Message;

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine("CLIENT CERTIFICATE LOAD FAILED");
                Console.ResetColor();
                Console.WriteLine(ex.ToString());

                result.Probes.Add(
                    ProbeResult.Fail("P12 load", ex.Message));

                return result;
            }

            X509CertificateCollection clientCertificates =
                new X509CertificateCollection();

            clientCertificates.Add(clientCertificate);

            if (endpoint.ProbeTls)
            {
                result.Probes.Add(
                    await TlsHandshakeTester.TestSingleTlsVersionAsync(
                        endpoint,
                        "TLS 1.0",
                        SslProtocols.Tls,
                        clientCertificates));

                result.Probes.Add(
                    await TlsHandshakeTester.TestSingleTlsVersionAsync(
                        endpoint,
                        "TLS 1.1",
                        SslProtocols.Tls11,
                        clientCertificates));

                result.Probes.Add(
                    await TlsHandshakeTester.TestSingleTlsVersionAsync(
                        endpoint,
                        "TLS 1.2",
                        SslProtocols.Tls12,
                        clientCertificates));

                result.Probes.Add(
                    await TlsHandshakeTester.TestSingleTlsVersionAsync(
                        endpoint,
                        "TLS 1.3",
                        TlsHandshakeTester.Tls13,
                        clientCertificates));
            }

            if (endpoint.ProbeHttpGet)
            {
                result.Probes.Add(
                    await HttpProbeTester.GetAsync(
                        endpoint,
                        clientCertificate,
                        "HTTP GET",
                        endpoint.Url));
            }

            if (endpoint.ProbeWsdl)
            {
                string wsdlUrl = AppendQuery(endpoint.Url, "wsdl");

                result.Probes.Add(
                    await HttpProbeTester.GetAsync(
                        endpoint,
                        clientCertificate,
                        "WSDL GET",
                        wsdlUrl));
            }

            if (endpoint.ProbeSoap)
            {
                result.Probes.Add(
                    await HttpProbeTester.SoapAsync(
                        endpoint,
                        clientCertificate));
            }

            return result;
        }

        private static void PrintSummary(List<InterfaceResult> results)
        {
            Console.WriteLine();
            Console.WriteLine("============================================================");
            Console.WriteLine("SUMMARY");
            Console.WriteLine("============================================================");
            Console.WriteLine(
                "Interface PASS = at least one TLS version handshake succeeded,");
            Console.WriteLine(
                "and any HTTP/SOAP probe received a response (including SOAP faults).");
            Console.WriteLine(
                "TLS 1.0 / 1.1 failures are expected on modern servers.");

            for (int i = 0; i < results.Count; i++)
            {
                InterfaceResult result = results[i];

                Console.WriteLine();
                Console.WriteLine(result.Endpoint.Name + "  " + result.Endpoint.Url);

                if (!result.CertificateLoaded)
                {
                    WriteStatus("  P12", false);
                    Console.WriteLine("  " + result.CertificateError);
                    continue;
                }

                for (int p = 0; p < result.Probes.Count; p++)
                {
                    ProbeResult probe = result.Probes[p];
                    WriteStatus("  " + probe.Name, probe.Success, probe.Connected);
                    Console.WriteLine("    " + probe.Detail);
                }

                Console.Write("  Interface result: ");

                if (result.Passed)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("PASS");
                    Console.ResetColor();
                }
                else if (result.ConnectionSucceeded)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("CONNECTED WITH ERRORS");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("FAIL");
                    Console.ResetColor();
                }
            }

            Console.WriteLine();
            Console.WriteLine("All tests completed");
        }

        private static void WriteStatus(string label, bool success, bool connected)
        {
            Console.Write(label.PadRight(36) + " : ");

            if (success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("PASS");
            }
            else if (connected)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("CONNECTED");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAIL");
            }

            Console.ResetColor();
        }

        private static bool IsListRequest(string[] args)
        {
            return args != null &&
                   args.Length == 1 &&
                   (String.Equals(args[0], "--list", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(args[0], "/list", StringComparison.OrdinalIgnoreCase));
        }

        private static void PrintInterfaceList(List<InterfaceEndpoint> endpoints)
        {
            Console.WriteLine("Configured interfaces:");

            for (int i = 0; i < endpoints.Count; i++)
            {
                Console.WriteLine(
                    "  " + endpoints[i].Name +
                    (endpoints[i].Enabled ? "" : " (disabled)") +
                    "  " + endpoints[i].Url);
            }
        }

        private static List<InterfaceEndpoint> FilterEndpoints(
            List<InterfaceEndpoint> endpoints,
            string[] args)
        {
            List<InterfaceEndpoint> enabled = new List<InterfaceEndpoint>();

            for (int i = 0; i < endpoints.Count; i++)
            {
                if (endpoints[i].Enabled)
                {
                    enabled.Add(endpoints[i]);
                }
            }

            if (args == null || args.Length == 0)
            {
                return enabled;
            }

            List<InterfaceEndpoint> matched = new List<InterfaceEndpoint>();

            for (int a = 0; a < args.Length; a++)
            {
                string requested = args[a];
                InterfaceEndpoint found = null;

                for (int i = 0; i < enabled.Count; i++)
                {
                    if (String.Equals(
                        enabled[i].Name,
                        requested,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        found = enabled[i];
                        break;
                    }
                }

                if (found == null)
                {
                    throw new InvalidOperationException(
                        "Unknown interface '" + requested +
                        "'. Use --list to see configured names.");
                }

                matched.Add(found);
            }

            return matched;
        }

        private static string DescribeProbes(InterfaceEndpoint endpoint)
        {
            List<string> probes = new List<string>();

            if (endpoint.ProbeTls)
            {
                probes.Add("tls");
            }

            if (endpoint.ProbeHttpGet)
            {
                probes.Add("http");
            }

            if (endpoint.ProbeWsdl)
            {
                probes.Add("wsdl");
            }

            if (endpoint.ProbeSoap)
            {
                probes.Add("soap");
            }

            return String.Join(", ", probes.ToArray());
        }

        private static string AppendQuery(string url, string query)
        {
            if (url.IndexOf('?') >= 0)
            {
                return url + "&" + query;
            }

            return url + "?" + query;
        }

        private static string ResolveInterfacesFile()
        {
            string configured = AppSettings.InterfacesFile;

            if (Path.IsPathRooted(configured) && File.Exists(configured))
            {
                return configured;
            }

            string fromBase = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                configured);

            if (File.Exists(fromBase))
            {
                return fromBase;
            }

            string fromCurrent = Path.GetFullPath(configured);

            if (File.Exists(fromCurrent))
            {
                return fromCurrent;
            }

            throw new FileNotFoundException(
                "Interfaces file was not found. Looked in: " + fromBase,
                configured);
        }

        private static void ConfigureServicePointManager()
        {
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.DefaultConnectionLimit = 50;
            ServicePointManager.CheckCertificateRevocationList =
                AppSettings.CheckCertificateRevocation;

            /*
             * Enable every TLS version the OS will allow. Individual handshake
             * tests still pin a single protocol via SslStream.
             */
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls |
                SecurityProtocolType.Tls11 |
                SecurityProtocolType.Tls12 |
                (SecurityProtocolType)12288;
        }
    }
}
