using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace InterfaceTester
{
    internal static class TestLog
    {
        private static TextWriter _originalOut;
        private static TextWriter _originalError;
        private static StreamWriter _logFile;
        private static bool _started;

        public static string SessionDirectory { get; private set; }

        public static string RunLogPath
        {
            get
            {
                if (String.IsNullOrEmpty(SessionDirectory))
                {
                    return null;
                }

                return Path.Combine(SessionDirectory, "run.log");
            }
        }

        public static void Start()
        {
            if (_started)
            {
                return;
            }

            string root = ResolveLogDirectory();
            Directory.CreateDirectory(root);

            SessionDirectory = Path.Combine(
                root,
                DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));

            Directory.CreateDirectory(SessionDirectory);

            _logFile = new StreamWriter(
                RunLogPath,
                false,
                new UTF8Encoding(false));
            _logFile.AutoFlush = true;

            _originalOut = Console.Out;
            _originalError = Console.Error;

            TeeTextWriter tee = new TeeTextWriter(_originalOut, _logFile);
            Console.SetOut(tee);
            Console.SetError(tee);

            _started = true;

            Console.WriteLine("Session log directory : " + SessionDirectory);
        }

        public static void SaveApiResponse(
            string interfaceName,
            string probeName,
            string contentType,
            int? httpStatus,
            string responseBody,
            string returnValue)
        {
            if (!_started)
            {
                return;
            }

            string prefix = SafeFileName(interfaceName + "_" + probeName);
            string extension = ChooseExtension(contentType, responseBody);
            string responsePath = Path.Combine(
                SessionDirectory,
                prefix + "_response" + extension);

            File.WriteAllText(
                responsePath,
                responseBody ?? "",
                new UTF8Encoding(false));

            string returnPath = Path.Combine(
                SessionDirectory,
                prefix + "_return_value.txt");

            StringBuilder returnFile = new StringBuilder();
            returnFile.AppendLine("Interface   : " + interfaceName);
            returnFile.AppendLine("Probe       : " + probeName);
            returnFile.AppendLine(
                "HTTP status : " +
                (httpStatus.HasValue ? httpStatus.Value.ToString() : "(none)"));
            returnFile.AppendLine(
                "Content-Type: " +
                (contentType ?? "(none)"));
            returnFile.AppendLine("Saved at    : " + DateTime.Now.ToString("o"));
            returnFile.AppendLine();
            returnFile.AppendLine("API Test return value:");
            returnFile.AppendLine(
                String.IsNullOrWhiteSpace(returnValue)
                    ? "(empty or not present)"
                    : returnValue);

            File.WriteAllText(
                returnPath,
                returnFile.ToString(),
                new UTF8Encoding(false));

            Console.WriteLine("  Response saved : " + responsePath);
            Console.WriteLine("  Return value saved : " + returnPath);
        }

        public static void WriteSummary(List<InterfaceResult> results)
        {
            if (!_started)
            {
                return;
            }

            string summaryPath = Path.Combine(SessionDirectory, "summary.txt");
            StringBuilder text = new StringBuilder();

            text.AppendLine("Interface Tester summary");
            text.AppendLine("Saved at: " + DateTime.Now.ToString("o"));
            text.AppendLine();

            for (int i = 0; i < results.Count; i++)
            {
                InterfaceResult result = results[i];
                string status;

                if (result.Passed)
                {
                    status = "PASS";
                }
                else if (result.ConnectionSucceeded)
                {
                    status = "CONNECTED WITH ERRORS";
                }
                else
                {
                    status = "FAIL";
                }

                text.AppendLine("============================================================");
                text.AppendLine(result.Endpoint.Name);
                text.AppendLine(result.Endpoint.Url);
                text.AppendLine("Result: " + status);

                if (!result.CertificateLoaded)
                {
                    text.AppendLine("P12: " + result.CertificateError);
                    text.AppendLine();
                    continue;
                }

                for (int p = 0; p < result.Probes.Count; p++)
                {
                    ProbeResult probe = result.Probes[p];
                    string probeStatus = probe.Success
                        ? "PASS"
                        : (probe.Connected ? "CONNECTED" : "FAIL");

                    text.AppendLine();
                    text.AppendLine("  " + probe.Name + " : " + probeStatus);
                    text.AppendLine("    " + probe.Detail);

                    if (!String.IsNullOrWhiteSpace(probe.ReturnValue))
                    {
                        text.AppendLine("    API return value: " + probe.ReturnValue);
                    }
                }

                text.AppendLine();
            }

            File.WriteAllText(summaryPath, text.ToString(), new UTF8Encoding(false));
            Console.WriteLine();
            Console.WriteLine("Summary saved : " + summaryPath);
        }

        public static void Finish()
        {
            if (!_started)
            {
                return;
            }

            string session = SessionDirectory;
            string runLog = RunLogPath;

            try
            {
                Console.WriteLine();
                Console.WriteLine("Full test log : " + runLog);
            }
            catch
            {
            }

            if (_originalOut != null)
            {
                Console.SetOut(_originalOut);
            }

            if (_originalError != null)
            {
                Console.SetError(_originalError);
            }

            if (_logFile != null)
            {
                _logFile.Flush();
                _logFile.Dispose();
                _logFile = null;
            }

            _started = false;

            if (!String.IsNullOrEmpty(session))
            {
                Console.WriteLine();
                Console.WriteLine("Test log folder : " + session);
                Console.WriteLine("Full test log   : " + runLog);
            }
        }

        private static string ResolveLogDirectory()
        {
            string configured = AppSettings.LogDirectory;

            if (Path.IsPathRooted(configured))
            {
                return configured;
            }

            return Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured));
        }

        private static string SafeFileName(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                bool bad = current == ' ' || current == '/' || current == '\\';

                if (!bad)
                {
                    for (int n = 0; n < invalid.Length; n++)
                    {
                        if (current == invalid[n])
                        {
                            bad = true;
                            break;
                        }
                    }
                }

                builder.Append(bad ? '_' : current);
            }

            return builder.ToString();
        }

        private static string ChooseExtension(string contentType, string body)
        {
            string type = (contentType ?? "").ToLowerInvariant();
            string text = (body ?? "").TrimStart();

            if (type.IndexOf("json", StringComparison.Ordinal) >= 0 ||
                text.StartsWith("{") ||
                text.StartsWith("["))
            {
                return ".json";
            }

            if (type.IndexOf("xml", StringComparison.Ordinal) >= 0 ||
                text.StartsWith("<"))
            {
                return ".xml";
            }

            return ".txt";
        }
    }

    internal sealed class TeeTextWriter : TextWriter
    {
        private readonly TextWriter _primary;
        private readonly TextWriter _secondary;

        public TeeTextWriter(TextWriter primary, TextWriter secondary)
        {
            _primary = primary;
            _secondary = secondary;
        }

        public override Encoding Encoding
        {
            get { return Encoding.UTF8; }
        }

        public override void Write(char value)
        {
            _primary.Write(value);
            _secondary.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            _primary.Write(buffer, index, count);
            _secondary.Write(buffer, index, count);
        }

        public override void Write(string value)
        {
            _primary.Write(value);
            _secondary.Write(value);
        }

        public override void WriteLine()
        {
            _primary.WriteLine();
            _secondary.WriteLine();
        }

        public override void WriteLine(string value)
        {
            _primary.WriteLine(value);
            _secondary.WriteLine(value);
        }

        public override void Flush()
        {
            _primary.Flush();
            _secondary.Flush();
        }
    }
}
