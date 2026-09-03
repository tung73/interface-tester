using System;
using System.Collections.Generic;

namespace InterfaceTester
{
    internal sealed class ProbeResult
    {
        public string Name { get; set; }
        public bool Success { get; set; }
        public bool Connected { get; set; }
        public string Detail { get; set; }
        public string ResponseBody { get; set; }
        public string ReturnValue { get; set; }
        public int? HttpStatus { get; set; }
        public string ContentType { get; set; }

        public static ProbeResult Pass(string name, string detail)
        {
            ProbeResult result = new ProbeResult();
            result.Name = name;
            result.Success = true;
            result.Connected = true;
            result.Detail = detail;
            return result;
        }

        public static ProbeResult Fail(string name, string detail)
        {
            ProbeResult result = new ProbeResult();
            result.Name = name;
            result.Success = false;
            result.Connected = false;
            result.Detail = detail;
            return result;
        }

        public static ProbeResult Http(
            string name,
            bool connected,
            bool success,
            string detail,
            string responseBody,
            string returnValue,
            int? httpStatus,
            string contentType)
        {
            ProbeResult result = new ProbeResult();
            result.Name = name;
            result.Connected = connected;
            result.Success = success;
            result.Detail = detail;
            result.ResponseBody = responseBody;
            result.ReturnValue = returnValue;
            result.HttpStatus = httpStatus;
            result.ContentType = contentType;
            return result;
        }
    }

    internal sealed class InterfaceResult
    {
        public InterfaceEndpoint Endpoint { get; set; }
        public bool CertificateLoaded { get; set; }
        public string CertificateError { get; set; }
        public List<ProbeResult> Probes { get; private set; }

        public InterfaceResult(InterfaceEndpoint endpoint)
        {
            Endpoint = endpoint;
            Probes = new List<ProbeResult>();
        }

        public bool Passed
        {
            get
            {
                if (!CertificateLoaded)
                {
                    return false;
                }

                if (Probes.Count == 0)
                {
                    return false;
                }

                bool tlsRequired = false;
                bool tlsSucceeded = false;
                bool applicationRequired = false;
                bool applicationConnected = false;

                for (int i = 0; i < Probes.Count; i++)
                {
                    ProbeResult probe = Probes[i];

                    if (IsTlsProbe(probe.Name))
                    {
                        tlsRequired = true;

                        if (probe.Success)
                        {
                            tlsSucceeded = true;
                        }
                    }
                    else
                    {
                        applicationRequired = true;

                        if (probe.Connected)
                        {
                            applicationConnected = true;
                        }
                    }
                }

                if (tlsRequired && !tlsSucceeded)
                {
                    return false;
                }

                if (applicationRequired && !applicationConnected)
                {
                    return false;
                }

                return true;
            }
        }

        private static bool IsTlsProbe(string name)
        {
            return name != null &&
                   name.StartsWith("TLS ", StringComparison.OrdinalIgnoreCase);
        }

        public bool ConnectionSucceeded
        {
            get
            {
                for (int i = 0; i < Probes.Count; i++)
                {
                    if (Probes[i].Connected)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
