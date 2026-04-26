using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KryossAgent.Services;

internal static partial class ServiceInstaller
{
    private const string ServiceName = "KryossAgent";
    private const string DisplayName = "Kryoss Security Agent";
    private const string Description = "Kryoss continuous security assessment service (TeamLogic IT)";

    private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;
    private const uint SERVICE_WIN32_OWN_PROCESS = 0x10;
    private const uint SERVICE_AUTO_START = 0x2;
    private const uint SERVICE_ERROR_NORMAL = 0x1;
    private const uint SERVICE_CONFIG_DESCRIPTION = 1;
    private const uint SERVICE_CONFIG_FAILURE_ACTIONS = 2;
    private const int SC_ACTION_RESTART = 1;
    private const uint SERVICE_CONTROL_STOP = 0x1;
    private const uint DELETE = 0x10000;
    private const uint SERVICE_QUERY_STATUS = 0x4;

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "CreateServiceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr CreateService(IntPtr hSCManager, string serviceName, string displayName,
        uint desiredAccess, uint serviceType, uint startType, uint errorControl,
        string binaryPathName, string? loadOrderGroup, IntPtr tagId,
        string? dependencies, string? serviceStartName, string? password);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr OpenService(IntPtr hSCManager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "DeleteService", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteService(IntPtr hService);

    [LibraryImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StartService(IntPtr hService, uint numServiceArgs, IntPtr serviceArgVectors);

    [LibraryImport("advapi32.dll", EntryPoint = "ControlService", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ControlService(IntPtr hService, uint control, out SERVICE_STATUS status);

    [LibraryImport("advapi32.dll", EntryPoint = "CloseServiceHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(IntPtr hSCObject);

    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig2(IntPtr hService, uint infoLevel, ref SERVICE_DESCRIPTION info);

    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig2ForRecovery(IntPtr hService, uint infoLevel, ref SERVICE_FAILURE_ACTIONS info);

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SERVICE_DESCRIPTION
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SC_ACTION
    {
        public int Type;
        public uint Delay;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_FAILURE_ACTIONS
    {
        public uint dwResetPeriod;
        public IntPtr lpRebootMsg;
        public IntPtr lpCommand;
        public uint cActions;
        public IntPtr lpsaActions;
    }

    public static void Install()
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine executable path");
        var binPath = $"\"{exePath}\" --service";

        var scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
        if (scm == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var svc = CreateService(scm, ServiceName, DisplayName,
                SERVICE_ALL_ACCESS, SERVICE_WIN32_OWN_PROCESS, SERVICE_AUTO_START, SERVICE_ERROR_NORMAL,
                binPath, null, IntPtr.Zero, null, null, null);

            if (svc == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                if (err == 1073) // ERROR_SERVICE_EXISTS
                {
                    Console.WriteLine($"  Service '{ServiceName}' already exists.");
                    return;
                }
                throw new Win32Exception(err);
            }

            try
            {
                var desc = new SERVICE_DESCRIPTION { lpDescription = Description };
                ChangeServiceConfig2(svc, SERVICE_CONFIG_DESCRIPTION, ref desc);

                var actions = new SC_ACTION[]
                {
                    new() { Type = SC_ACTION_RESTART, Delay = 5000 },
                    new() { Type = SC_ACTION_RESTART, Delay = 10000 },
                    new() { Type = SC_ACTION_RESTART, Delay = 30000 },
                };
                var actionsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SC_ACTION>() * actions.Length);
                try
                {
                    for (int i = 0; i < actions.Length; i++)
                        Marshal.StructureToPtr(actions[i], actionsPtr + i * Marshal.SizeOf<SC_ACTION>(), false);
                    var failureActions = new SERVICE_FAILURE_ACTIONS
                    {
                        dwResetPeriod = 86400,
                        lpRebootMsg = IntPtr.Zero,
                        lpCommand = IntPtr.Zero,
                        cActions = (uint)actions.Length,
                        lpsaActions = actionsPtr,
                    };
                    ChangeServiceConfig2ForRecovery(svc, SERVICE_CONFIG_FAILURE_ACTIONS, ref failureActions);
                }
                finally { Marshal.FreeHGlobal(actionsPtr); }

                StartService(svc, 0, IntPtr.Zero);
                Console.WriteLine($"  Service '{ServiceName}' installed and started.");
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }

    public static bool IsInstalled()
    {
        var scm = OpenSCManager(null, null, SERVICE_QUERY_STATUS);
        if (scm == IntPtr.Zero) return false;
        try
        {
            var svc = OpenService(scm, ServiceName, SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return false;
            CloseServiceHandle(svc);
            return true;
        }
        finally { CloseServiceHandle(scm); }
    }

    public static void Uninstall()
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
        if (scm == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var svc = OpenService(scm, ServiceName, SERVICE_ALL_ACCESS | DELETE);
            if (svc == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                if (err == 1060) // ERROR_SERVICE_DOES_NOT_EXIST
                {
                    Console.WriteLine($"  Service '{ServiceName}' does not exist.");
                    return;
                }
                throw new Win32Exception(err);
            }

            try
            {
                ControlService(svc, SERVICE_CONTROL_STOP, out _);
                Thread.Sleep(2000);

                if (!DeleteService(svc))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                Console.WriteLine($"  Service '{ServiceName}' stopped and removed.");
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }
}
