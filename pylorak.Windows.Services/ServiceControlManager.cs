using System;
using System.ComponentModel;
using System.Security.Permissions;
using System.ServiceProcess;
using System.Runtime.InteropServices;

namespace pylorak.Windows.Services
{
    public class ServiceControlManager : IDisposable
    {
        private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
        private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
        private const uint SERVICE_ERROR_NORMAL = 0x00000001;
        private const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
        private const int ERROR_SERVICE_MARKED_FOR_DELETE = 1072;

        private bool disposed;
        private readonly SafeServiceHandle SCManager;

        private SafeServiceHandle OpenService(string serviceName, ServiceAccessRights desiredAccess)
        {
            // Open the service
            var service = NativeMethods.OpenService(
                SCManager,
                serviceName,
                desiredAccess);

            // Verify if the service is opened
            if (service.IsInvalid)
                throw new Win32Exception();

            return service;
        }

        [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
        public ServiceControlManager()
            : this(ServiceControlAccessRights.SC_MANAGER_CONNECT)
        {
        }

        [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
        public ServiceControlManager(ServiceControlAccessRights desiredAccess)
        {
            // Open the service control manager
            SCManager = NativeMethods.OpenSCManager(
                null,
                null,
                desiredAccess);

            // Verify if the SC is opened
            if (SCManager.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        /// <summary>
        /// Installs a Win32 own-process service running as LocalSystem with
        /// the given start mode and dependencies. If a service with the same
        /// name already exists, its binary path, display name, dependencies,
        /// and start mode are updated in place (semantics: update-if-exists).
        /// </summary>
        [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
        public void InstallService(
            string serviceName,
            string displayName,
            string binaryPath,
            string[]? dependencies,
            ServiceStartMode startMode)
        {
            char[]? depBuf = BuildDependencyBuffer(dependencies);

            // First, try to open an existing service and update it.
            using (var existing = NativeMethods.OpenService(
                SCManager,
                serviceName,
                ServiceAccessRights.SERVICE_CHANGE_CONFIG | ServiceAccessRights.SERVICE_QUERY_CONFIG))
            {
                if (!existing.IsInvalid)
                {
                    var ok = NativeMethods.ChangeServiceConfig(
                        existing,
                        SERVICE_NO_CHANGE,
                        (uint)startMode,
                        SERVICE_NO_CHANGE,
                        binaryPath,
                        null,
                        IntPtr.Zero,
                        depBuf,
                        null,
                        null,
                        displayName);
                    if (!ok)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    return;
                }

                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_SERVICE_DOES_NOT_EXIST)
                    throw new Win32Exception(err);
            }

            // Service does not exist — create it.
            using var created = NativeMethods.CreateService(
                SCManager,
                serviceName,
                displayName,
                ServiceAccessRights.SERVICE_ALL_ACCESS,
                SERVICE_WIN32_OWN_PROCESS,
                (uint)startMode,
                SERVICE_ERROR_NORMAL,
                binaryPath,
                null,
                IntPtr.Zero,
                depBuf,
                null,  // null = LocalSystem
                null);

            if (created.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        /// <summary>
        /// Removes the named service. No-op if it doesn't exist.
        /// </summary>
        [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
        public void UninstallService(string serviceName)
        {
            using var service = NativeMethods.OpenService(
                SCManager,
                serviceName,
                ServiceAccessRights.DELETE);

            if (service.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_SERVICE_DOES_NOT_EXIST)
                    return;
                throw new Win32Exception(err);
            }

            if (!NativeMethods.DeleteService(service))
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_SERVICE_MARKED_FOR_DELETE)
                    return;
                throw new Win32Exception(err);
            }
        }

        private static char[]? BuildDependencyBuffer(string[]? dependencies)
        {
            if (dependencies is null || dependencies.Length == 0)
                return null;

            // Win32 expects a double-null-terminated MULTI_SZ.
            int len = 0;
            foreach (var d in dependencies)
                len += d.Length + 1;
            len += 1;

            var buf = new char[len];
            int pos = 0;
            foreach (var d in dependencies)
            {
                d.CopyTo(0, buf, pos, d.Length);
                pos += d.Length;
                buf[pos++] = '\0';
            }
            buf[pos] = '\0';
            return buf;
        }

        /*
        /// <summary>
        /// Dertermines whether the nominated service is set to restart on failure.
        /// </summary>
        /// <exception cref="ComponentModel.Win32Exception">"Unable to query the Service configuration."</exception>
        [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
        internal bool HasRestartOnFailure(string serviceName)
        {
            const int bufferSize = 1024 * 8;

            IntPtr service = IntPtr.Zero;
            IntPtr bufferPtr = IntPtr.Zero;
            bool result = false;

            try
            {
                // Open the service
                service = OpenService(serviceName, ServiceAccessRights.SERVICE_QUERY_CONFIG);

                int dwBytesNeeded = 0;

                // Allocate memory for struct
                bufferPtr = Marshal.AllocHGlobal(bufferSize);
                int queryResult = NativeMethods.QueryServiceConfig2(
                    service,
                    ServiceConfig2InfoLevel.SERVICE_CONFIG_FAILURE_ACTIONS,
                    bufferPtr,
                    bufferSize,
                    out dwBytesNeeded);

                if (queryResult == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                // Cast the buffer to a QUERY_SERVICE_CONFIG struct
                SERVICE_FAILURE_ACTIONS config =
                    (SERVICE_FAILURE_ACTIONS)Marshal.PtrToStructure(bufferPtr, typeof(SERVICE_FAILURE_ACTIONS));

                // Determine whether the service is set to auto restart
                if (config.cActions != 0)
                {
                    SC_ACTION action = (SC_ACTION)Marshal.PtrToStructure(config.lpsaActions, typeof(SC_ACTION));
                    result = (action.Type == SC_ACTION_TYPE.SC_ACTION_RESTART);
                }                

                return result;
            }
            finally
            {
                // Clean up
                if (bufferPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(bufferPtr);
                }

                if (service != IntPtr.Zero)
                {
                    NativeMethods.CloseServiceHandle(service);
                }
            }
        }
        */

        /// <summary>
        /// Sets the nominated service to restart on failure.
        /// </summary>
        [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
        public void SetRestartOnFailure(string serviceName, bool restartOnFailure)
        {
            const uint delay = 1000;
            const int MAX_ACTIONS = 2;
            int SC_ACTION_SIZE = Marshal.SizeOf<SC_ACTION>();

            // Open the service
            using var service = OpenService(
                serviceName,
                ServiceAccessRights.SERVICE_CHANGE_CONFIG |
                ServiceAccessRights.SERVICE_START);

            using var actionPtr = SafeHGlobalHandle.Alloc(SC_ACTION_SIZE * MAX_ACTIONS);
            int actionCount;
            if (restartOnFailure)
            {
                actionCount = 2;

                // Set up the restart action
                var action1 = new SC_ACTION();
                action1.Type = SC_ACTION_TYPE.SC_ACTION_RESTART;
                action1.Delay = delay;
                actionPtr.MarshalFromStruct(action1, 0);

                // Set up the "do nothing" action
                var action2 = new SC_ACTION();
                action2.Type = SC_ACTION_TYPE.SC_ACTION_NONE;
                action2.Delay = delay;
                actionPtr.MarshalFromStruct(action2, SC_ACTION_SIZE);
            }
            else
            {
                actionCount = 1;

                // Set up the "do nothing" action
                var action1 = new SC_ACTION();
                action1.Type = SC_ACTION_TYPE.SC_ACTION_NONE;
                action1.Delay = delay;
                actionPtr.MarshalFromStruct(action1);
            }

            // Set up the failure actions
            var failureActions = new SERVICE_FAILURE_ACTIONS();
            failureActions.dwResetPeriod = 0;
            failureActions.cActions = (uint)actionCount;
            failureActions.lpsaActions = actionPtr.DangerousGetHandle();
            failureActions.lpRebootMsg = null;
            failureActions.lpCommand = null;
            using var failureActionsPtr = SafeHGlobalHandle.FromManagedStruct(failureActions);

            // Make the change
            int changeResult = NativeMethods.ChangeServiceConfig2(
                service,
                ServiceConfig2InfoLevel.SERVICE_CONFIG_FAILURE_ACTIONS,
                failureActionsPtr.DangerousGetHandle());
            if (changeResult == 0)
                throw new Win32Exception();
        }

        [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
        public void SetStartupMode(string serviceName, ServiceStartMode mode)
        {
            using var service = OpenService(
                serviceName,
                ServiceAccessRights.SERVICE_CHANGE_CONFIG |
                ServiceAccessRights.SERVICE_QUERY_CONFIG
            );
            var result = NativeMethods.ChangeServiceConfig(
                service,
                SERVICE_NO_CHANGE,
                (uint)mode,
                SERVICE_NO_CHANGE,
                null,
                null,
                IntPtr.Zero,
                null,
                null,
                null,
                null);

            if (result == false)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
        public void SetLoadOrderGroup(string serviceName, string group)
        {
            using var service = OpenService(
                serviceName,
                ServiceAccessRights.SERVICE_CHANGE_CONFIG |
                ServiceAccessRights.SERVICE_QUERY_CONFIG
            );
            var result = NativeMethods.ChangeServiceConfig(
                service,
                SERVICE_NO_CHANGE,
                SERVICE_NO_CHANGE,
                SERVICE_NO_CHANGE,
                null,
                group,
                IntPtr.Zero,
                null,
                null,
                null,
                null);

            if (result == false)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
        public uint GetStartupMode(string serviceName)
        {
            using var service = OpenService(serviceName, ServiceAccessRights.SERVICE_QUERY_CONFIG);

            var result = NativeMethods.QueryServiceConfig(service, IntPtr.Zero, 0, out uint structSize);
            using var buff = SafeHGlobalHandle.Alloc(structSize);

            result = NativeMethods.QueryServiceConfig(service, buff.DangerousGetHandle(), structSize, out structSize);
            if (result == false)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            QUERY_SERVICE_CONFIG query_srv_config = Marshal.PtrToStructure<QUERY_SERVICE_CONFIG>(buff.DangerousGetHandle());
            return query_srv_config.dwStartType;
        }

        [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
        public uint? GetServicePid(string serviceName)
        {
            using var service = OpenService(serviceName, ServiceAccessRights.SERVICE_QUERY_STATUS);

            var result = NativeMethods.QueryServiceStatusEx(service, ServiceInfoLevel.SC_STATUS_PROCESS_INFO, IntPtr.Zero, 0, out uint structSize);
            using var buff = SafeHGlobalHandle.Alloc(structSize);

            result = NativeMethods.QueryServiceStatusEx(service, ServiceInfoLevel.SC_STATUS_PROCESS_INFO, buff.DangerousGetHandle(), structSize, out structSize);
            if (result == false)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            SERVICE_STATUS_PROCESS query_srv_status = Marshal.PtrToStructure<SERVICE_STATUS_PROCESS>(buff.DangerousGetHandle());

            return query_srv_status.dwCurrentState switch
            {
                ServiceState.Running or
                ServiceState.PausePending or
                ServiceState.Paused or
                ServiceState.ContinuePending => query_srv_status.dwProcessId,
                _ => null,
            };
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                // Release managed resources

                SCManager.Dispose();
            }

            // Release unmanaged resources.
            // Set large fields to null.
            // Call Dispose on your base class.

            disposed = true;
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
