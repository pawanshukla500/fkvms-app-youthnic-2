using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Youthnic.Desktop.Services;

namespace Youthnic.Desktop;

public partial class App : Application
{
    private static Mutex? _instanceMutex;
    private static IntPtr _jobHandle = IntPtr.Zero;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = "Global\\YouthnicPackingDesk_SingleInstance_Mutex";
        _instanceMutex = new Mutex(true, mutexName, out var isOnlyInstance);
        if (!isOnlyInstance)
        {
            // Another instance of Youthnic Packing Desk is already running
            NativeMethods.BringExistingInstanceToFront();
            Shutdown(0);
            Environment.Exit(0);
            return;
        }

        // Enable Windows Job Object limit to ensure all child processes (WebView2, etc.)
        // terminate immediately when the main process exits.
        EnableKillOnJobClose();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
        }
        catch { }

        base.OnExit(e);
        Environment.Exit(0);
    }

    private static void EnableKillOnJobClose()
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

            _jobHandle = CreateJobObject(IntPtr.Zero, null);
            if (_jobHandle == IntPtr.Zero) return;

            var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            };
            var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = info
            };

            int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
                if (SetInformationJobObject(_jobHandle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length))
                {
                    AssignProcessToJobObject(_jobHandle, Process.GetCurrentProcess().Handle);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(extendedInfoPtr);
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug($"App: JobObject init skipped: {ex.Message}");
        }
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        public static void BringExistingInstanceToFront()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                var other = Process.GetProcessesByName(current.ProcessName)
                    .FirstOrDefault(p => p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero);
                if (other != null)
                {
                    ShowWindow(other.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(other.MainWindowHandle);
                }
            }
            catch { }
        }
    }
}
