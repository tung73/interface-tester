using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace InterfaceTester
{
    internal static class ServerCertificateValidator
    {
        public static RemoteCertificateValidationCallback CreateCallback(
            bool acceptUntrusted)
        {
            return (sender, certificate, chain, sslPolicyErrors) =>
                ValidateServerCertificate(
                    certificate,
                    chain,
                    sslPolicyErrors,
                    acceptUntrusted);
        }

        public static bool ValidateServerCertificate(
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors,
            bool acceptUntrusted)
        {
            Console.WriteLine();
            Console.WriteLine("Server certificate validation:");

            if (certificate != null)
            {
                Console.WriteLine("  Subject : " + certificate.Subject);
                Console.WriteLine("  Issuer  : " + certificate.Issuer);
            }
            else
            {
                Console.WriteLine("  Server did not provide a certificate.");
            }

            Console.WriteLine("  Policy errors: " + sslPolicyErrors);

            if (chain != null && chain.ChainStatus != null)
            {
                foreach (X509ChainStatus chainStatus in chain.ChainStatus)
                {
                    Console.WriteLine(
                        "  Chain status : " + chainStatus.Status);

                    Console.WriteLine(
                        "  Status info  : " +
                        chainStatus.StatusInformation.Trim());
                }
            }

            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }

            if (acceptUntrusted)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(
                    "  Accepting certificate despite policy errors " +
                    "(acceptUntrustedServerCertificate=true).");
                Console.ResetColor();
                return true;
            }

            /*
             * IMPORTANT:
             * We are NOT accepting invalid/untrusted server certificates by default.
             *
             * CheckCertificateRevocation = false only stops online CRL/OCSP
             * certificate revocation checking in AuthenticateAsClientAsync().
             */
            return false;
        }
    }
}
