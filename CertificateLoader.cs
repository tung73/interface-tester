using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace InterfaceTester
{
    internal static class CertificateLoader
    {
        public static X509Certificate2 LoadClientCertificate(
            string p12Path,
            string p12Password)
        {
            if (!File.Exists(p12Path))
            {
                throw new FileNotFoundException(
                    "Client P12 certificate file was not found.",
                    p12Path);
            }

            /*
             * Same flags as the original CAPS tester. MachineKeySet + PersistKeySet
             * avoids "keyset does not exist" on some locked-down Windows images.
             */
            X509KeyStorageFlags keyStorageFlags =
                X509KeyStorageFlags.MachineKeySet |
                X509KeyStorageFlags.PersistKeySet |
                X509KeyStorageFlags.Exportable;

            X509Certificate2 certificate = new X509Certificate2(
                p12Path,
                p12Password ?? "",
                keyStorageFlags);

            if (!certificate.HasPrivateKey)
            {
                throw new InvalidOperationException(
                    "The P12 certificate was loaded but does not contain " +
                    "an accessible private key.");
            }

            return certificate;
        }

        public static void PrintClientCertificateInformation(
            X509Certificate2 certificate)
        {
            Console.WriteLine();
            Console.WriteLine("Client certificate:");
            Console.WriteLine("  Subject        : " + certificate.Subject);
            Console.WriteLine("  Issuer         : " + certificate.Issuer);
            Console.WriteLine("  Thumbprint     : " + certificate.Thumbprint);
            Console.WriteLine("  Serial number  : " + certificate.SerialNumber);
            Console.WriteLine(
                "  Valid from     : " +
                certificate.NotBefore.ToUniversalTime() + "Z");
            Console.WriteLine(
                "  Valid until    : " +
                certificate.NotAfter.ToUniversalTime() + "Z");
            Console.WriteLine("  Has private key: " + certificate.HasPrivateKey);
        }
    }
}
