using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace InterfaceTester
{
    internal static class SoapTlsProbeTester
    {
        private const int MaxResponseBytes = 2 * 1024 * 1024;

        public static async Task<ProbeResult> PostAsync(
            InterfaceEndpoint endpoint,
            X509CertificateCollection clientCertificates,
            string protocolName,
            SslProtocols requiredProtocol)
        {
            string probeName = String.IsNullOrWhiteSpace(endpoint.SoapAction)
                ? "SOAP testConnection " + protocolName
                : "SOAP " + protocolName;
            string soapEnvelope = endpoint.LoadSoapEnvelope();
            Uri uri = endpoint.ParsedUrl;
            string host = uri.Host;
            int port = uri.Port;
            string path = String.IsNullOrEmpty(uri.PathAndQuery)
                ? "/"
                : uri.PathAndQuery;

            Console.WriteLine();
            Console.WriteLine("------------------------------------------------------------");
            Console.WriteLine("TEST: " + probeName);
            Console.WriteLine("------------------------------------------------------------");
            Console.WriteLine("  URL     : " + endpoint.Url);
            Console.WriteLine("  Method  : POST");
            Console.WriteLine(
                "  SOAPAction: " +
                (String.IsNullOrEmpty(endpoint.SoapAction)
                    ? "(empty)"
                    : endpoint.SoapAction));
            Console.WriteLine("  TLS pin : " + protocolName + " only");

            try
            {
                using (TcpClient tcpClient = new TcpClient())
                {
                    Console.WriteLine("Connecting to " + host + ":" + port + " ...");

                    await WaitForTaskAsync(
                        tcpClient.ConnectAsync(host, port),
                        AppSettings.ConnectionTimeoutSeconds,
                        "TCP connection timed out after " +
                        AppSettings.ConnectionTimeoutSeconds + " seconds.");

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

                        await WaitForTaskAsync(
                            sslStream.AuthenticateAsClientAsync(
                                host,
                                clientCertificates,
                                requiredProtocol,
                                AppSettings.CheckCertificateRevocation),
                            AppSettings.TlsHandshakeTimeoutSeconds,
                            "TLS handshake timed out after " +
                            AppSettings.TlsHandshakeTimeoutSeconds +
                            " seconds.");

                        string negotiated =
                            TlsHandshakeTester.GetProtocolDisplayName(
                                sslStream.SslProtocol);

                        Console.WriteLine("  Negotiated TLS : " + negotiated);
                        Console.WriteLine(
                            "  Cipher         : " +
                            sslStream.CipherAlgorithm + " " +
                            sslStream.CipherStrength + " bits");

                        byte[] requestBytes = BuildHttpRequest(
                            endpoint,
                            host,
                            port,
                            path,
                            soapEnvelope);

                        Console.WriteLine("  Bytes   : " + requestBytes.Length);

                        await WriteWithTimeoutAsync(sslStream, requestBytes);

                        ParsedHttpResponse parsed =
                            await ReadHttpResponseAsync(sslStream);

                        string extraDetail =
                            "Negotiated " + negotiated +
                            " (pinned " + protocolName + ")";

                        return HttpProbeTester.ReportHttpResult(
                            endpoint,
                            probeName,
                            parsed.StatusCode,
                            parsed.ReasonPhrase,
                            parsed.ContentType,
                            parsed.Body,
                            extraDetail);
                    }
                }
            }
            catch (TimeoutException ex)
            {
                return PrintFailedTest(probeName, protocolName, ex);
            }
            catch (AuthenticationException ex)
            {
                return PrintFailedTest(probeName, protocolName, ex);
            }
            catch (SocketException ex)
            {
                return PrintFailedTest(probeName, protocolName, ex);
            }
            catch (Win32Exception ex)
            {
                return PrintFailedTest(probeName, protocolName, ex);
            }
            catch (IOException ex)
            {
                return PrintFailedTest(probeName, protocolName, ex);
            }
            catch (Exception ex)
            {
                return PrintFailedTest(probeName, protocolName, ex);
            }
        }

        private static byte[] BuildHttpRequest(
            InterfaceEndpoint endpoint,
            string host,
            int port,
            string path,
            string soapEnvelope)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(soapEnvelope);
            string hostHeader = port == 443 ? host : host + ":" + port;
            string contentType = String.IsNullOrWhiteSpace(endpoint.ContentType)
                ? "text/xml; charset=utf-8"
                : endpoint.ContentType;
            string soapAction = endpoint.SoapAction ?? "";

            StringBuilder request = new StringBuilder();
            request.Append("POST " + path + " HTTP/1.1\r\n");
            request.Append("Host: " + hostHeader + "\r\n");
            request.Append("User-Agent: InterfaceTester/1.0 (.NET Framework 4.8)\r\n");
            request.Append("Content-Type: " + contentType + "\r\n");
            request.Append("SOAPAction: \"" + soapAction + "\"\r\n");
            request.Append("Content-Length: " + bodyBytes.Length + "\r\n");
            request.Append("Connection: close\r\n");
            request.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(request.ToString());
            byte[] requestBytes = new byte[headerBytes.Length + bodyBytes.Length];
            Buffer.BlockCopy(headerBytes, 0, requestBytes, 0, headerBytes.Length);
            Buffer.BlockCopy(
                bodyBytes,
                0,
                requestBytes,
                headerBytes.Length,
                bodyBytes.Length);

            return requestBytes;
        }

        private static async Task<ParsedHttpResponse> ReadHttpResponseAsync(
            SslStream sslStream)
        {
            MemoryStream buffer = new MemoryStream();
            byte[] chunk = new byte[8192];

            while (buffer.Length < MaxResponseBytes)
            {
                int read = await ReadWithTimeoutAsync(sslStream, chunk, 0, chunk.Length);

                if (read <= 0)
                {
                    break;
                }

                buffer.Write(chunk, 0, read);

                ParsedHttpResponse complete;
                if (TryParseHttpResponse(buffer.ToArray(), out complete))
                {
                    return complete;
                }
            }

            ParsedHttpResponse parsed;
            if (TryParseHttpResponse(buffer.ToArray(), out parsed, true))
            {
                return parsed;
            }

            throw new InvalidOperationException(
                "The server closed the TLS connection before a complete HTTP response was received.");
        }

        private static bool TryParseHttpResponse(
            byte[] raw,
            out ParsedHttpResponse parsed)
        {
            return TryParseHttpResponse(raw, out parsed, false);
        }

        private static bool TryParseHttpResponse(
            byte[] raw,
            out ParsedHttpResponse parsed,
            bool connectionClosed)
        {
            parsed = null;

            int headerEnd = IndexOf(raw, Encoding.ASCII.GetBytes("\r\n\r\n"));

            if (headerEnd < 0)
            {
                return false;
            }

            string headerText = Encoding.ASCII.GetString(raw, 0, headerEnd);
            string[] headerLines = headerText.Split(
                new[] { "\r\n" },
                StringSplitOptions.None);

            if (headerLines.Length == 0)
            {
                return false;
            }

            string[] statusParts = headerLines[0].Split(new[] { ' ' }, 3);
            int statusCode;

            if (statusParts.Length < 2 ||
                !statusParts[0].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) ||
                !Int32.TryParse(statusParts[1], out statusCode))
            {
                throw new InvalidOperationException(
                    "The server returned an invalid HTTP status line: " +
                    headerLines[0]);
            }

            string reasonPhrase = statusParts.Length >= 3 ? statusParts[2] : "";
            string contentType = "";
            int contentLength = -1;
            bool chunked = false;

            for (int i = 1; i < headerLines.Length; i++)
            {
                int colon = headerLines[i].IndexOf(':');

                if (colon <= 0)
                {
                    continue;
                }

                string name = headerLines[i].Substring(0, colon).Trim();
                string value = headerLines[i].Substring(colon + 1).Trim();

                if (String.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = value;
                }
                else if (String.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    int parsedLength;

                    if (Int32.TryParse(value, out parsedLength))
                    {
                        contentLength = parsedLength;
                    }
                }
                else if (String.Equals(
                    name,
                    "Transfer-Encoding",
                    StringComparison.OrdinalIgnoreCase) &&
                    value.ToLowerInvariant().IndexOf("chunked", StringComparison.Ordinal) >= 0)
                {
                    chunked = true;
                }
            }

            int bodyOffset = headerEnd + 4;
            int bodyAvailable = raw.Length - bodyOffset;

            if (bodyAvailable < 0)
            {
                bodyAvailable = 0;
            }

            byte[] bodyBytes;

            if (chunked)
            {
                if (!TryDecodeChunked(raw, bodyOffset, out bodyBytes))
                {
                    return false;
                }
            }
            else if (contentLength >= 0)
            {
                if (bodyAvailable < contentLength)
                {
                    return false;
                }

                bodyBytes = new byte[contentLength];
                Buffer.BlockCopy(raw, bodyOffset, bodyBytes, 0, contentLength);
            }
            else if (connectionClosed)
            {
                bodyBytes = new byte[bodyAvailable];
                Buffer.BlockCopy(raw, bodyOffset, bodyBytes, 0, bodyAvailable);
            }
            else
            {
                return false;
            }

            parsed = new ParsedHttpResponse();
            parsed.StatusCode = statusCode;
            parsed.ReasonPhrase = reasonPhrase;
            parsed.ContentType = contentType;
            parsed.Body = Encoding.UTF8.GetString(bodyBytes);
            return true;
        }

        private static bool TryDecodeChunked(
            byte[] raw,
            int offset,
            out byte[] bodyBytes)
        {
            bodyBytes = null;
            MemoryStream body = new MemoryStream();
            int position = offset;

            while (position < raw.Length)
            {
                int lineEnd = IndexOf(raw, Encoding.ASCII.GetBytes("\r\n"), position);

                if (lineEnd < 0)
                {
                    return false;
                }

                string sizeLine = Encoding.ASCII.GetString(
                    raw,
                    position,
                    lineEnd - position);
                int semicolon = sizeLine.IndexOf(';');

                if (semicolon >= 0)
                {
                    sizeLine = sizeLine.Substring(0, semicolon);
                }

                int chunkSize;

                if (!Int32.TryParse(
                    sizeLine.Trim(),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out chunkSize))
                {
                    return false;
                }

                position = lineEnd + 2;

                if (chunkSize == 0)
                {
                    bodyBytes = body.ToArray();
                    return true;
                }

                if (position + chunkSize + 2 > raw.Length)
                {
                    return false;
                }

                body.Write(raw, position, chunkSize);
                position += chunkSize;

                if (raw[position] != (byte)'\r' || raw[position + 1] != (byte)'\n')
                {
                    return false;
                }

                position += 2;
            }

            return false;
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            return IndexOf(haystack, needle, 0);
        }

        private static int IndexOf(byte[] haystack, byte[] needle, int start)
        {
            if (needle.Length == 0 || haystack.Length - start < needle.Length)
            {
                return -1;
            }

            for (int i = start; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;

                for (int n = 0; n < needle.Length; n++)
                {
                    if (haystack[i + n] != needle[n])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        private static async Task WaitForTaskAsync(
            Task task,
            int timeoutSeconds,
            string timeoutMessage)
        {
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            Task completed = await Task.WhenAny(task, timeoutTask);

            if (completed != task)
            {
                throw new TimeoutException(timeoutMessage);
            }

            await task;
        }

        private static async Task WriteWithTimeoutAsync(SslStream stream, byte[] data)
        {
            Task writeTask = stream.WriteAsync(data, 0, data.Length);
            await WaitForTaskAsync(
                writeTask,
                AppSettings.HttpTimeoutSeconds,
                "Timed out writing the SOAP request after " +
                AppSettings.HttpTimeoutSeconds + " seconds.");
            await stream.FlushAsync();
        }

        private static async Task<int> ReadWithTimeoutAsync(
            SslStream stream,
            byte[] buffer,
            int offset,
            int count)
        {
            Task<int> readTask = stream.ReadAsync(buffer, offset, count);
            await WaitForTaskAsync(
                readTask,
                AppSettings.HttpTimeoutSeconds,
                "Timed out reading the SOAP response after " +
                AppSettings.HttpTimeoutSeconds + " seconds.");
            return await readTask;
        }

        private static ProbeResult PrintFailedTest(
            string probeName,
            string protocolName,
            Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("TEST FAILED");
            Console.ResetColor();

            Console.WriteLine("  Requested TLS    : " + protocolName);
            Console.WriteLine("  Exception type   : " + ex.GetType().FullName);
            Console.WriteLine("  Message          : " + ex.Message);
            Console.WriteLine();
            Console.WriteLine("  Full exception:");
            Console.WriteLine(ex.ToString());

            return ProbeResult.Fail(probeName, ex.Message);
        }

        private sealed class ParsedHttpResponse
        {
            public int StatusCode { get; set; }
            public string ReasonPhrase { get; set; }
            public string ContentType { get; set; }
            public string Body { get; set; }
        }
    }
}
