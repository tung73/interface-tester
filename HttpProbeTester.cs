using System;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace InterfaceTester
{
    internal static class HttpProbeTester
    {
        public static Task<ProbeResult> GetAsync(
            InterfaceEndpoint endpoint,
            X509Certificate2 clientCertificate,
            string probeName,
            string url)
        {
            return SendAsync(
                endpoint,
                clientCertificate,
                probeName,
                url,
                "GET",
                null,
                null,
                null);
        }

        public static Task<ProbeResult> SoapAsync(
            InterfaceEndpoint endpoint,
            X509Certificate2 clientCertificate)
        {
            string soapEnvelope = endpoint.LoadSoapEnvelope();
            string probeName = String.IsNullOrWhiteSpace(endpoint.SoapAction)
                ? "SOAP testConnection"
                : "SOAP " + endpoint.SoapAction;

            return SendAsync(
                endpoint,
                clientCertificate,
                probeName,
                endpoint.Url,
                "POST",
                endpoint.ContentType,
                endpoint.SoapAction ?? "",
                soapEnvelope);
        }

        private static async Task<ProbeResult> SendAsync(
            InterfaceEndpoint endpoint,
            X509Certificate2 clientCertificate,
            string probeName,
            string url,
            string method,
            string contentType,
            string soapAction,
            string body)
        {
            Console.WriteLine();
            Console.WriteLine("------------------------------------------------------------");
            Console.WriteLine("TEST: " + probeName);
            Console.WriteLine("------------------------------------------------------------");
            Console.WriteLine("  URL     : " + url);
            Console.WriteLine("  Method  : " + method);

            if (soapAction != null)
            {
                Console.WriteLine(
                    "  SOAPAction: " +
                    (soapAction.Length == 0 ? "(empty)" : soapAction));
            }

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.KeepAlive = false;
            request.AllowAutoRedirect = false;
            request.Timeout = AppSettings.HttpTimeoutSeconds * 1000;
            request.ReadWriteTimeout = AppSettings.HttpTimeoutSeconds * 1000;
            request.ProtocolVersion = HttpVersion.Version11;
            request.UserAgent = "InterfaceTester/1.0 (.NET Framework 4.8)";
            request.ClientCertificates.Add(clientCertificate);
            request.ServerCertificateValidationCallback =
                ServerCertificateValidator.CreateCallback(
                    endpoint.AcceptUntrusted);

            if (!String.IsNullOrWhiteSpace(contentType))
            {
                request.ContentType = contentType;
            }

            if (soapAction != null)
            {
                request.Headers.Add("SOAPAction", "\"" + soapAction + "\"");
            }

            try
            {
                if (body != null)
                {
                    byte[] payload = Encoding.UTF8.GetBytes(body);
                    request.ContentLength = payload.Length;

                    Console.WriteLine("  Bytes   : " + payload.Length);

                    using (Stream requestStream = await request.GetRequestStreamAsync())
                    {
                        await requestStream.WriteAsync(payload, 0, payload.Length);
                    }
                }

                using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
                {
                    return PrintHttpResponse(endpoint, probeName, response);
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse errorResponse = ex.Response as HttpWebResponse;

                if (errorResponse != null)
                {
                    using (errorResponse)
                    {
                        /*
                         * An HTTP status or SOAP fault still proves that TCP, TLS,
                         * client-certificate auth, and HTTP all succeeded.
                         */
                        return PrintHttpResponse(endpoint, probeName, errorResponse);
                    }
                }

                return PrintHttpFailure(probeName, ex);
            }
            catch (Exception ex)
            {
                return PrintHttpFailure(probeName, ex);
            }
        }

        private static ProbeResult PrintHttpResponse(
            InterfaceEndpoint endpoint,
            string probeName,
            HttpWebResponse response)
        {
            string body = ReadBody(response);
            int statusCode = (int)response.StatusCode;
            bool soapFault = IsSoapFault(body);
            bool success = statusCode >= 200 && statusCode < 400 && !soapFault;
            string returnValue = SoapResponseParser.ExtractReturnValue(body);

            string detail =
                (int)response.StatusCode + " " + response.StatusCode +
                " (" + response.ContentType + ")";

            if (soapFault)
            {
                detail += "; SOAP Fault";
            }

            if (!String.IsNullOrWhiteSpace(returnValue))
            {
                detail += "; return=" + Truncate(returnValue, 120);
            }

            if (success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("TEST SUCCESS");
                Console.ResetColor();
            }
            else if (statusCode > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("CONNECTED — HTTP/SOAP response received");
                Console.ResetColor();
            }

            Console.WriteLine("  Status      : " + statusCode + " " + response.StatusCode);
            Console.WriteLine("  Content-Type: " + response.ContentType);
            Console.WriteLine("  SOAP fault  : " + soapFault);

            if (!String.IsNullOrWhiteSpace(returnValue))
            {
                Console.WriteLine("  API return value:");
                Console.WriteLine(returnValue);
            }
            else
            {
                Console.WriteLine("  API return value: (not present in response)");
            }

            PrintBodyPreview(body);

            TestLog.SaveApiResponse(
                endpoint.Name,
                probeName,
                response.ContentType,
                statusCode,
                body,
                returnValue);

            return ProbeResult.Http(
                probeName,
                true,
                success,
                detail,
                body,
                returnValue,
                statusCode,
                response.ContentType);
        }

        private static ProbeResult PrintHttpFailure(string probeName, Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("TEST FAILED");
            Console.ResetColor();

            Console.WriteLine("  Exception type : " + ex.GetType().FullName);
            Console.WriteLine("  Message        : " + ex.Message);

            Console.WriteLine();
            Console.WriteLine("  Full exception:");
            Console.WriteLine(ex.ToString());

            return ProbeResult.Fail(probeName, ex.Message);
        }

        private static string ReadBody(HttpWebResponse response)
        {
            Stream stream = response.GetResponseStream();

            if (stream == null)
            {
                return "";
            }

            using (stream)
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static bool IsSoapFault(string body)
        {
            if (String.IsNullOrWhiteSpace(body))
            {
                return false;
            }

            string lower = body.ToLowerInvariant();

            return lower.IndexOf(":fault", StringComparison.Ordinal) >= 0 ||
                   lower.IndexOf("<fault", StringComparison.Ordinal) >= 0;
        }

        private static void PrintBodyPreview(string body)
        {
            if (String.IsNullOrWhiteSpace(body))
            {
                Console.WriteLine("  Body         : (empty)");
                return;
            }

            string preview = body.Trim();
            int maxChars = AppSettings.ResponsePreviewChars;

            if (preview.Length > maxChars)
            {
                preview = preview.Substring(0, maxChars) +
                          Environment.NewLine +
                          "... [truncated, " + body.Length + " characters total]";
            }

            Console.WriteLine("  Body preview :");
            Console.WriteLine(preview);
        }

        private static string Truncate(string value, int maxChars)
        {
            if (String.IsNullOrEmpty(value) || value.Length <= maxChars)
            {
                return value;
            }

            return value.Substring(0, maxChars) + "...";
        }
    }
}
