using System;
using System.ComponentModel;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace InterfaceTester
{
    internal static class TlsHandshakeTester
    {
        /*
         * TLS 1.3 numeric value is 12288.
         *
         * SslProtocols.Tls13 is unavailable in older .NET Framework builds,
         * so this cast allows the program to compile in those environments.
         */
        public static readonly SslProtocols Tls13 = (SslProtocols)12288;

        public static async Task<ProbeResult> TestSingleTlsVersionAsync(
            InterfaceEndpoint endpoint,
            string protocolName,
            SslProtocols requiredProtocol,
            X509CertificateCollection clientCertificates)
        {
            Console.WriteLine();
            Console.WriteLine("------------------------------------------------------------");
            Console.WriteLine("TEST: " + protocolName + " only");
            Console.WriteLine("------------------------------------------------------------");

            Uri uri = endpoint.ParsedUrl;
            string host = uri.Host;
            int port = uri.Port;

            try
            {
                using (TcpClient tcpClient = new TcpClient())
                {
                    Console.WriteLine(
                        "Connecting to " + host + ":" + port + " ...");

                    Task connectTask = tcpClient.ConnectAsync(host, port);

                    Task timeoutTask = Task.Delay(
                        TimeSpan.FromSeconds(AppSettings.ConnectionTimeoutSeconds));

                    Task completedTask = await Task.WhenAny(
                        connectTask,
                        timeoutTask);

                    if (completedTask != connectTask)
                    {
                        throw new TimeoutException(
                            "TCP connection timed out after " +
                            AppSettings.ConnectionTimeoutSeconds +
                            " seconds.");
                    }

                    await connectTask;

                    using (NetworkStream networkStream = tcpClient.GetStream())
                    using (SslStream sslStream = new SslStream(
                        networkStream,
                        false,
                        ServerCertificateValidator.CreateCallback(
                            endpoint.AcceptUntrusted)))
                    {
                        Console.WriteLine(
                            "Starting mutual TLS handshake; " +
                            protocolName + " only...");

                        Console.WriteLine(
                            "Certificate revocation checking: " +
                            (AppSettings.CheckCertificateRevocation
                                ? "ENABLED"
                                : "DISABLED"));

                        Task authenticateTask =
                            sslStream.AuthenticateAsClientAsync(
                                host,
                                clientCertificates,
                                requiredProtocol,
                                AppSettings.CheckCertificateRevocation);

                        Task handshakeTimeoutTask = Task.Delay(
                            TimeSpan.FromSeconds(
                                AppSettings.TlsHandshakeTimeoutSeconds));

                        Task completedHandshakeTask = await Task.WhenAny(
                            authenticateTask,
                            handshakeTimeoutTask);

                        if (completedHandshakeTask != authenticateTask)
                        {
                            throw new TimeoutException(
                                "TLS handshake timed out after " +
                                AppSettings.TlsHandshakeTimeoutSeconds +
                                " seconds. TCP connected successfully, " +
                                "but the server did not complete the " +
                                protocolName + " mutual TLS handshake.");
                        }

                        await authenticateTask;

                        string detail =
                            "Negotiated " +
                            GetProtocolDisplayName(sslStream.SslProtocol) +
                            ", cipher " + sslStream.CipherAlgorithm +
                            " " + sslStream.CipherStrength + " bits";

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("TEST SUCCESS");
                        Console.ResetColor();

                        Console.WriteLine(
                            "  Requested protocol  : " + protocolName);

                        Console.WriteLine(
                            "  Negotiated protocol : " +
                            GetProtocolDisplayName(sslStream.SslProtocol));

                        Console.WriteLine(
                            "  Cipher algorithm    : " +
                            sslStream.CipherAlgorithm);

                        Console.WriteLine(
                            "  Cipher strength     : " +
                            sslStream.CipherStrength + " bits");

                        Console.WriteLine(
                            "  Hash algorithm      : " +
                            sslStream.HashAlgorithm);

                        Console.WriteLine(
                            "  Hash strength       : " +
                            sslStream.HashStrength + " bits");

                        Console.WriteLine(
                            "  Key exchange        : " +
                            sslStream.KeyExchangeAlgorithm);

                        Console.WriteLine(
                            "  Key exchange strength: " +
                            sslStream.KeyExchangeStrength + " bits");

                        return ProbeResult.Pass(protocolName, detail);
                    }
                }
            }
            catch (TimeoutException ex)
            {
                return PrintFailedTest(protocolName, ex);
            }
            catch (AuthenticationException ex)
            {
                return PrintFailedTest(protocolName, ex);
            }
            catch (SocketException ex)
            {
                return PrintFailedTest(protocolName, ex);
            }
            catch (Win32Exception ex)
            {
                return PrintFailedTest(protocolName, ex);
            }
            catch (Exception ex)
            {
                return PrintFailedTest(protocolName, ex);
            }
        }

        private static ProbeResult PrintFailedTest(
            string protocolName,
            Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("TEST FAILED");
            Console.ResetColor();

            Console.WriteLine("  Requested protocol : " + protocolName);
            Console.WriteLine("  Exception type     : " + ex.GetType().FullName);
            Console.WriteLine("  Message            : " + ex.Message);

            Console.WriteLine();
            Console.WriteLine("  Full exception:");
            Console.WriteLine(ex.ToString());

            return ProbeResult.Fail(protocolName, ex.Message);
        }

        public static string GetProtocolDisplayName(SslProtocols protocol)
        {
            if ((int)protocol == 12288)
            {
                return "TLS 1.3";
            }

            if (protocol == SslProtocols.Tls12)
            {
                return "TLS 1.2";
            }

            if (protocol == SslProtocols.Tls11)
            {
                return "TLS 1.1";
            }

            if (protocol == SslProtocols.Tls)
            {
                return "TLS 1.0";
            }

            return protocol.ToString();
        }
    }
}
