using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace InterfaceTester
{
    internal static class SchannelTrace
    {
        /*
         * netsh trace only accepts providers from its catalog.
         * "Microsoft-Windows-Schannel" is not in that list.
         * These GUIDs match `netsh trace show providers | findstr schannel`.
         */
        private const string ProviderSchannelEvents =
            "{91CC1150-71AA-47E2-AE18-C96E61736B6F}";
        private const string ProviderSchannel =
            "{1F678132-5938-4686-9FDC-C8FF68F15C85}";
        private const string ProviderSecuritySchannel =
            "{37D2C3CD-C5D4-4587-8531-4696C44244C8}";

        private static bool _started;
        private static string _sessionDirectory;
        private static string _etlPath;
        private static string _textPath;
        private static string _excerptPath;
        private static string _netshLogPath;

        public static string ExcerptPath
        {
            get { return _excerptPath; }
        }

        public static string TextPath
        {
            get { return _textPath; }
        }

        public static void TryStart()
        {
            if (_started || !AppSettings.CaptureSchannelTrace)
            {
                return;
            }

            if (String.IsNullOrEmpty(TestLog.SessionDirectory))
            {
                return;
            }

            _sessionDirectory = TestLog.SessionDirectory;
            _etlPath = Path.Combine(_sessionDirectory, "schannel.etl");
            _textPath = Path.Combine(_sessionDirectory, "schannel.txt");
            _excerptPath = Path.Combine(_sessionDirectory, "schannel_tls.txt");
            _netshLogPath = Path.Combine(_sessionDirectory, "schannel_netsh.log");

            if (!IsWindows())
            {
                WriteSkip("Schannel netsh trace is Windows-only.");
                return;
            }

            if (!IsAdministrator())
            {
                WriteSkip(
                    "Schannel netsh trace needs Administrator. " +
                    "Run Visual Studio or InterfaceTester.exe as Administrator, " +
                    "or set CaptureSchannelTrace to false.");
                return;
            }

            string capture = AppSettings.CaptureSchannelPackets ? "yes" : "no";
            string arguments =
                "trace start capture=" + capture +
                " provider=" + ProviderSchannelEvents +
                " provider=" + ProviderSchannel +
                " provider=" + ProviderSecuritySchannel +
                " tracefile=\"" + _etlPath + "\"" +
                " maxSize=200 overwrite=yes report=no";

            Console.WriteLine(
                "Schannel netsh trace          : starting (capture=" +
                capture + ")");

            CommandResult result = RunNetsh(arguments, 30);

            AppendNetshLog("START", arguments, result);

            if (result.ExitCode != 0)
            {
                WriteSkip(
                    "netsh trace start failed (exit " + result.ExitCode +
                    "). See schannel_netsh.log. Tests continue without the OS trace.");
                return;
            }

            _started = true;
            Console.WriteLine("Schannel etl                  : " + _etlPath);
            Console.WriteLine("netsh                         : " + ResolveNetshPath());
        }

        public static void TryStop()
        {
            if (!_started)
            {
                return;
            }

            _started = false;

            Console.WriteLine();
            Console.WriteLine("Schannel netsh trace          : stopping...");

            CommandResult stopResult = RunNetsh("trace stop", 60);
            AppendNetshLog("STOP", "trace stop", stopResult);

            if (stopResult.ExitCode != 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(
                    "netsh trace stop failed (exit " + stopResult.ExitCode +
                    "). See schannel_netsh.log.");
                Console.ResetColor();
                return;
            }

            if (!File.Exists(_etlPath))
            {
                string foundEtl = FindEtlNearSession();

                if (!String.IsNullOrEmpty(foundEtl))
                {
                    try
                    {
                        File.Copy(foundEtl, _etlPath, true);
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine(
                            "Could not copy netsh etl into the log folder: " +
                            ex.Message);
                        Console.ResetColor();
                        _etlPath = foundEtl;
                    }
                }
            }

            if (!File.Exists(_etlPath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(
                    "netsh trace stopped, but schannel.etl was not found.");
                Console.ResetColor();
                return;
            }

            string convertArguments =
                "trace convert input=\"" + _etlPath + "\"" +
                " output=\"" + _textPath + "\" dump=TXT report=no";

            CommandResult convertResult = RunNetsh(convertArguments, 120);
            AppendNetshLog("CONVERT", convertArguments, convertResult);

            if (convertResult.ExitCode != 0 || !File.Exists(_textPath))
            {
                TryFallbackConvert();
            }

            if (File.Exists(_textPath))
            {
                WriteExcerpt();
                Console.WriteLine("Schannel text                 : " + _textPath);
                Console.WriteLine("Schannel TLS excerpt          : " + _excerptPath);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(
                    "netsh trace convert did not produce schannel.txt. " +
                    "The etl is still in the log folder.");
                Console.ResetColor();
            }
        }

        public static void AppendToSummary(StringBuilder text)
        {
            if (text == null || String.IsNullOrEmpty(_sessionDirectory))
            {
                return;
            }

            bool any =
                File.Exists(_etlPath) ||
                File.Exists(_textPath) ||
                File.Exists(_excerptPath) ||
                File.Exists(_netshLogPath);

            if (!any)
            {
                return;
            }

            text.AppendLine("Schannel netsh trace");

            if (File.Exists(_etlPath))
            {
                text.AppendLine("  etl     : " + _etlPath);
            }

            if (File.Exists(_textPath))
            {
                text.AppendLine("  text    : " + _textPath);
            }

            if (File.Exists(_excerptPath))
            {
                text.AppendLine("  excerpt : " + _excerptPath);
            }

            if (File.Exists(_netshLogPath))
            {
                text.AppendLine("  netsh   : " + _netshLogPath);
            }

            text.AppendLine();
        }

        private static void TryFallbackConvert()
        {
            string fallback =
                "trace convert \"" + _etlPath + "\"";
            CommandResult result = RunNetsh(fallback, 120);
            AppendNetshLog("CONVERT-FALLBACK", fallback, result);

            if (File.Exists(_textPath))
            {
                return;
            }

            string defaultTxt = Path.ChangeExtension(_etlPath, ".txt");

            if (File.Exists(defaultTxt) &&
                !String.Equals(defaultTxt, _textPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(defaultTxt, _textPath, true);
            }
        }

        private static void WriteExcerpt()
        {
            StringBuilder excerpt = new StringBuilder();
            excerpt.AppendLine("Schannel TLS excerpt");
            excerpt.AppendLine("Source : " + _textPath);
            excerpt.AppendLine("Saved  : " + DateTime.Now.ToString("o"));
            excerpt.AppendLine();

            int kept = 0;
            string[] lines = File.ReadAllLines(_textPath);

            for (int i = 0; i < lines.Length; i++)
            {
                if (!IsTlsRelated(lines[i]))
                {
                    continue;
                }

                excerpt.AppendLine(lines[i]);
                kept++;

                if (kept >= 500)
                {
                    excerpt.AppendLine();
                    excerpt.AppendLine("... truncated after 500 matching lines ...");
                    break;
                }
            }

            if (kept == 0)
            {
                excerpt.AppendLine(
                    "(No TLS/SSL/Schannel lines were found in the converted trace.)");
            }

            File.WriteAllText(_excerptPath, excerpt.ToString(), new UTF8Encoding(false));
        }

        private static bool IsTlsRelated(string line)
        {
            if (String.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string lower = line.ToLowerInvariant();

            return lower.IndexOf("tls", StringComparison.Ordinal) >= 0 ||
                   lower.IndexOf("ssl", StringComparison.Ordinal) >= 0 ||
                   lower.IndexOf("schannel", StringComparison.Ordinal) >= 0 ||
                   lower.IndexOf("cipher", StringComparison.Ordinal) >= 0 ||
                   lower.IndexOf("handshake", StringComparison.Ordinal) >= 0;
        }

        private static string FindEtlNearSession()
        {
            if (String.IsNullOrEmpty(_sessionDirectory) ||
                !Directory.Exists(_sessionDirectory))
            {
                return null;
            }

            string[] files = Directory.GetFiles(_sessionDirectory, "*.etl");

            if (files.Length > 0)
            {
                return files[0];
            }

            return null;
        }

        private static CommandResult RunNetsh(string arguments, int timeoutSeconds)
        {
            CommandResult result = new CommandResult();
            string netsh = ResolveNetshPath();

            if (String.IsNullOrEmpty(netsh) || !File.Exists(netsh))
            {
                result.ExitCode = -1;
                result.Output = "64-bit netsh.exe was not found. Looked for Sysnative and System32.";
                return result;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = netsh;
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            startInfo.WorkingDirectory = _sessionDirectory ??
                AppDomain.CurrentDomain.BaseDirectory;

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    StringBuilder output = new StringBuilder();

                    process.OutputDataReceived += delegate(
                        object sender,
                        DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                        {
                            lock (output)
                            {
                                output.AppendLine(e.Data);
                            }
                        }
                    };

                    process.ErrorDataReceived += delegate(
                        object sender,
                        DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                        {
                            lock (output)
                            {
                                output.AppendLine(e.Data);
                            }
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(timeoutSeconds * 1000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }

                        result.ExitCode = -1;
                        result.Output = output.ToString() +
                            "Timed out after " + timeoutSeconds + " seconds.";
                        return result;
                    }

                    process.WaitForExit();
                    result.ExitCode = process.ExitCode;
                    result.Output = output.ToString();
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                result.Output = ex.ToString();
                return result;
            }
        }

        private static void AppendNetshLog(
            string step,
            string arguments,
            CommandResult result)
        {
            if (String.IsNullOrEmpty(_netshLogPath))
            {
                return;
            }

            StringBuilder text = new StringBuilder();
            text.AppendLine("============================================================");
            text.AppendLine(step + "  " + DateTime.Now.ToString("o"));
            text.AppendLine("netsh.exe: " + ResolveNetshPath());
            text.AppendLine("netsh " + arguments);
            text.AppendLine("exit code: " + result.ExitCode);
            text.AppendLine();
            text.AppendLine(result.Output ?? "");
            text.AppendLine();

            File.AppendAllText(_netshLogPath, text.ToString(), new UTF8Encoding(false));
        }

        private static void WriteSkip(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Schannel netsh trace          : skipped");
            Console.WriteLine("  " + message);
            Console.ResetColor();
        }

        private static bool IsWindows()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
        }

        /*
         * 32-bit netsh (SysWOW64) has no "trace" command. A 32-bit process
         * must use Sysnative to reach the 64-bit System32 netsh.
         */
        private static string ResolveNetshPath()
        {
            string windows = Environment.GetFolderPath(
                Environment.SpecialFolder.Windows);

            if (!Environment.Is64BitProcess && Environment.Is64BitOperatingSystem)
            {
                string sysnative = Path.Combine(windows, "Sysnative", "netsh.exe");

                if (File.Exists(sysnative))
                {
                    return sysnative;
                }
            }

            string system32 = Path.Combine(windows, "System32", "netsh.exe");

            if (File.Exists(system32))
            {
                return system32;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "netsh.exe");
        }

        private static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();

                if (identity == null)
                {
                    return false;
                }

                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private sealed class CommandResult
        {
            public int ExitCode { get; set; }
            public string Output { get; set; }
        }
    }
}
