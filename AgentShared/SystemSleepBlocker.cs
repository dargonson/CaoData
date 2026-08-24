// BO SUNG QUAN LY NGUON DUNG CHUNG: chi chan system sleep, khong giu man hinh sang va khong chan shutdown.
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace AgentShared
{
    public static class SystemSleepBlocker
    {
        private const uint PowerRequestContextVersion = 0;
        private const uint PowerRequestContextSimpleString = 0x1;
        private static readonly object SyncRoot = new object();
        private static SafeFileHandle? _requestHandle;
        private static int _leaseCount;

        public static IDisposable PreventSystemSleep(string reason)
        {
            if (!OperatingSystem.IsWindows())
            {
                return NoopLease.Instance;
            }

            lock (SyncRoot)
            {
                if (_leaseCount == 0)
                {
                    ReasonContext context = new ReasonContext
                    {
                        Version = PowerRequestContextVersion,
                        Flags = PowerRequestContextSimpleString,
                        SimpleReasonString = string.IsNullOrWhiteSpace(reason)
                            ? "Agent file transfer is active."
                            : reason
                    };

                    SafeFileHandle handle = PowerCreateRequest(ref context);
                    if (handle.IsInvalid || !PowerSetRequest(handle, PowerRequestType.SystemRequired))
                    {
                        handle.Dispose();
                        return NoopLease.Instance;
                    }

                    _requestHandle = handle;
                }

                _leaseCount++;
                return new SleepLease();
            }
        }

        private static void Release()
        {
            lock (SyncRoot)
            {
                if (_leaseCount <= 0)
                {
                    return;
                }

                _leaseCount--;
                if (_leaseCount != 0)
                {
                    return;
                }

                if (_requestHandle != null && !_requestHandle.IsInvalid)
                {
                    PowerClearRequest(_requestHandle, PowerRequestType.SystemRequired);
                }

                _requestHandle?.Dispose();
                _requestHandle = null;
            }
        }

        private sealed class SleepLease : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    Release();
                }
            }
        }

        private sealed class NoopLease : IDisposable
        {
            public static readonly NoopLease Instance = new NoopLease();

            public void Dispose()
            {
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ReasonContext
        {
            public uint Version;
            public uint Flags;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string SimpleReasonString;
        }

        private enum PowerRequestType
        {
            SystemRequired = 0
        }

        [DllImport("PowrProf.dll", SetLastError = true)]
        private static extern SafeFileHandle PowerCreateRequest(ref ReasonContext context);

        [DllImport("PowrProf.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PowerSetRequest(SafeFileHandle powerRequest, PowerRequestType requestType);

        [DllImport("PowrProf.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PowerClearRequest(SafeFileHandle powerRequest, PowerRequestType requestType);
    }
}
