using System;
using System.Collections.Generic;
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
            finally
            {
                TestLog.Finish();
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
            if (!IsListRequest(args))
            {
                TestLog.Start();
            }

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
            Console.WriteLine("Interfaces configured in     : App.config");
            Console.WriteLine();

            List<InterfaceEndpoint> allEndpoints =
                InterfaceEndpoint.LoadFromAppConfig();

            if (IsListRequest(args))
            {
                PrintInterfaceMenu(allEndpoints);
                return 0;
            }

            List<InterfaceEndpoint> endpoints = SelectEndpoints(allEndpoints, args);

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
            TestLog.WriteSummary(results);

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
            Console.WriteLine(
                "INTERFACE " + endpoint.Number + ": " + endpoint.Name);
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
                    await SoapTlsProbeTester.PostAsync(
                        endpoint,
                        clientCertificates,
                        "TLS 1.2",
                        SslProtocols.Tls12));

                result.Probes.Add(
                    await SoapTlsProbeTester.PostAsync(
                        endpoint,
                        clientCertificates,
                        "TLS 1.3",
                        TlsHandshakeTester.Tls13));
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
                "SOAP is posted twice: TLS 1.2 only, then TLS 1.3 only.");
            Console.WriteLine(
                "TLS 1.0 / 1.1 handshake failures are expected on modern servers.");

            for (int i = 0; i < results.Count; i++)
            {
                InterfaceResult result = results[i];

                Console.WriteLine();
                Console.WriteLine(
                    result.Endpoint.Number + ". " +
                    result.Endpoint.Name + "  " + result.Endpoint.Url);

                if (!result.CertificateLoaded)
                {
                    WriteStatus("  P12", false, false);
                    Console.WriteLine("  " + result.CertificateError);
                    continue;
                }

                for (int p = 0; p < result.Probes.Count; p++)
                {
                    ProbeResult probe = result.Probes[p];
                    WriteStatus("  " + probe.Name, probe.Success, probe.Connected);
                    Console.WriteLine("    " + probe.Detail);

                    if (!String.IsNullOrWhiteSpace(probe.ReturnValue))
                    {
                        Console.WriteLine("    API return value: " + probe.ReturnValue);
                    }
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

        private static void PrintInterfaceMenu(List<InterfaceEndpoint> endpoints)
        {
            Console.WriteLine("Which interface do you want to test?");
            Console.WriteLine();

            for (int i = 0; i < endpoints.Count; i++)
            {
                InterfaceEndpoint endpoint = endpoints[i];

                Console.WriteLine(
                    "  " + endpoint.Number + ". " + endpoint.Name +
                    (endpoint.Enabled ? "" : " (disabled)"));
                Console.WriteLine("     " + endpoint.Url);
            }

            Console.WriteLine("  A. Test all");
            Console.WriteLine();
        }

        private static List<InterfaceEndpoint> SelectEndpoints(
            List<InterfaceEndpoint> endpoints,
            string[] args)
        {
            if (args != null && args.Length > 0)
            {
                return ResolveSelection(endpoints, args[0], true);
            }

            if (Console.IsInputRedirected)
            {
                string redirected = Console.ReadLine();

                if (!String.IsNullOrWhiteSpace(redirected))
                {
                    return ResolveSelection(endpoints, redirected.Trim(), true);
                }

                Console.WriteLine(
                    "Input is redirected. Testing all enabled interfaces.");
                Console.WriteLine();
                return EnabledOnly(endpoints);
            }

            while (true)
            {
                PrintInterfaceMenu(endpoints);
                Console.Write("Enter " + ChoiceHint(endpoints) + ": ");

                string choice = Console.ReadLine();

                if (choice == null)
                {
                    return EnabledOnly(endpoints);
                }

                choice = choice.Trim();

                if (choice.Length == 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Please enter " + ChoiceHint(endpoints) + ".");
                    Console.WriteLine();
                    continue;
                }

                try
                {
                    return ResolveSelection(endpoints, choice, false);
                }
                catch (InvalidOperationException ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine();
                    Console.WriteLine(ex.Message);
                    Console.ResetColor();
                    Console.WriteLine();
                }
            }
        }

        private static List<InterfaceEndpoint> ResolveSelection(
            List<InterfaceEndpoint> endpoints,
            string choice,
            bool fromArgs)
        {
            if (IsAllChoice(choice))
            {
                List<InterfaceEndpoint> allEnabled = EnabledOnly(endpoints);

                Console.WriteLine("Selected: all enabled interfaces.");
                Console.WriteLine();
                return allEnabled;
            }

            InterfaceEndpoint found = FindEndpoint(endpoints, choice);

            if (found == null)
            {
                string hint = fromArgs
                    ? " Use " + ChoiceHint(endpoints) + ", or an interface name."
                    : " Enter " + ChoiceHint(endpoints) + ".";

                throw new InvalidOperationException(
                    "Unknown choice '" + choice + "'." + hint);
            }

            if (!found.Enabled)
            {
                throw new InvalidOperationException(
                    "Interface " + found.Number + " (" + found.Name +
                    ") is disabled in App.config.");
            }

            Console.WriteLine(
                "Selected: " + found.Number + ". " + found.Name);
            Console.WriteLine();

            List<InterfaceEndpoint> selected = new List<InterfaceEndpoint>();
            selected.Add(found);
            return selected;
        }

        private static InterfaceEndpoint FindEndpoint(
            List<InterfaceEndpoint> endpoints,
            string choice)
        {
            int number;

            if (Int32.TryParse(choice, out number))
            {
                for (int i = 0; i < endpoints.Count; i++)
                {
                    if (endpoints[i].Number == number)
                    {
                        return endpoints[i];
                    }
                }

                return null;
            }

            for (int i = 0; i < endpoints.Count; i++)
            {
                if (String.Equals(
                    endpoints[i].Name,
                    choice,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return endpoints[i];
                }
            }

            return null;
        }

        private static string ChoiceHint(List<InterfaceEndpoint> endpoints)
        {
            List<string> parts = new List<string>();

            for (int i = 0; i < endpoints.Count; i++)
            {
                parts.Add(endpoints[i].Number.ToString());
            }

            return String.Join(", ", parts.ToArray()) + ", or A";
        }

        private static bool IsAllChoice(string choice)
        {
            return String.Equals(choice, "A", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(choice, "all", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(choice, "*", StringComparison.OrdinalIgnoreCase);
        }

        private static List<InterfaceEndpoint> EnabledOnly(
            List<InterfaceEndpoint> endpoints)
        {
            List<InterfaceEndpoint> enabled = new List<InterfaceEndpoint>();

            for (int i = 0; i < endpoints.Count; i++)
            {
                if (endpoints[i].Enabled)
                {
                    enabled.Add(endpoints[i]);
                }
            }

            return enabled;
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
