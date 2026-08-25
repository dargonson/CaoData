using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AgentService
{
    /// <summary>
    /// BO SUNG MODULE REMOTE FILE: service Windows nam o Session 0, vi vay lenh
    /// mo file duoc chuyen sang desktop cua user dang dang nhap.
    /// </summary>
    internal static class InteractiveSessionLauncher
    {
        private const uint InvalidSessionId = 0xFFFFFFFF;
        private const uint TokenAllAccess = 0xF01FF;
        private const int SecurityIdentification = 1;
        private const int TokenPrimary = 1;
        private const uint CreateUnicodeEnvironment = 0x00000400;
        private const uint CreateNoWindow = 0x08000000;

        internal static void OpenPath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (Environment.UserInteractive)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fullPath)
                {
                    UseShellExecute = true
                });
                return;
            }

            uint sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == InvalidSessionId)
            {
                throw new InvalidOperationException("Không có desktop user đang hoạt động để mở file.");
            }

            IntPtr userToken = IntPtr.Zero;
            IntPtr primaryToken = IntPtr.Zero;
            IntPtr environment = IntPtr.Zero;
            PROCESS_INFORMATION processInfo = default;
            try
            {
                if (!WTSQueryUserToken(sessionId, out userToken))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Không lấy được token user đang đăng nhập.");
                }
                if (!DuplicateTokenEx(
                        userToken,
                        TokenAllAccess,
                        IntPtr.Zero,
                        SecurityIdentification,
                        TokenPrimary,
                        out primaryToken))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Không tạo được token tiến trình user.");
                }
                if (!CreateEnvironmentBlock(out environment, primaryToken, false))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Không tạo được môi trường tiến trình user.");
                }

                string executable = Environment.ProcessPath
                    ?? throw new InvalidOperationException("Không xác định được AgentServices.exe.");
                string commandLine;
                if (Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                {
                    string[] commandLineArgs = Environment.GetCommandLineArgs();
                    string assembly = commandLineArgs.Length > 0
                        ? Path.GetFullPath(commandLineArgs[0])
                        : string.Empty;
                    if (!File.Exists(assembly))
                    {
                        throw new InvalidOperationException("Không xác định được AgentServices.dll.");
                    }
                    commandLine = $"{Quote(executable)} {Quote(assembly)} --open-path {Quote(fullPath)}";
                }
                else
                {
                    commandLine = $"{Quote(executable)} --open-path {Quote(fullPath)}";
                }

                STARTUPINFO startup = new STARTUPINFO
                {
                    cb = Marshal.SizeOf<STARTUPINFO>(),
                    lpDesktop = @"winsta0\default"
                };
                var mutableCommand = new StringBuilder(commandLine);
                if (!CreateProcessAsUser(
                        primaryToken,
                        null,
                        mutableCommand,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        CreateUnicodeEnvironment | CreateNoWindow,
                        environment,
                        AppContext.BaseDirectory,
                        ref startup,
                        out processInfo))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Không mở được file trong phiên user.");
                }
            }
            finally
            {
                if (processInfo.hThread != IntPtr.Zero) CloseHandle(processInfo.hThread);
                if (processInfo.hProcess != IntPtr.Zero) CloseHandle(processInfo.hProcess);
                if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
                if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
                if (userToken != IntPtr.Zero) CloseHandle(userToken);
            }
        }

        private static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (ch == '"')
                {
                    result.Append('\\', backslashes * 2 + 1).Append('"');
                    backslashes = 0;
                    continue;
                }

                result.Append('\\', backslashes).Append(ch);
                backslashes = 0;
            }
            result.Append('\\', backslashes * 2).Append('"');
            return result.ToString();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DuplicateTokenEx(
            IntPtr existingToken,
            uint desiredAccess,
            IntPtr tokenAttributes,
            int impersonationLevel,
            int tokenType,
            out IntPtr newToken);

        [DllImport("userenv.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateEnvironmentBlock(
            out IntPtr environment,
            IntPtr token,
            [MarshalAs(UnmanagedType.Bool)] bool inherit);

        [DllImport("userenv.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyEnvironmentBlock(IntPtr environment);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcessAsUser(
            IntPtr token,
            string? applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref STARTUPINFO startupInfo,
            out PROCESS_INFORMATION processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
