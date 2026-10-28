using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;

namespace MiniHardwareWidget.Services;

internal static class DriverServiceHelper
{
    private const string DriverDevice = @"\\.\WinRing0_1_2_0";
    private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;
    private const uint SERVICE_KERNEL_DRIVER = 0x00000001;
    private const uint SERVICE_DEMAND_START = 0x00000003;
    private const uint SERVICE_ERROR_NORMAL = 0x00000001;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;

    private const string ExpectedDriverSha256 = "11BD2C9F9E2397C9A16E0990E4ED2CF0679498FE0FD418A3DFDAC60B5C160EE5";

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenSCManagerW")]
    private static extern IntPtr OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateServiceW")]
    private static extern IntPtr CreateService(
        IntPtr hSCManager,
        string lpServiceName,
        string lpDisplayName,
        uint dwDesiredAccess,
        uint dwServiceType,
        uint dwStartType,
        uint dwErrorControl,
        string lpBinaryPathName,
        string? lpLoadOrderGroup,
        IntPtr lpdwTagId,
        string? lpDependencies,
        string? lpServiceStartName,
        string? lpPassword);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenServiceW")]
    private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "StartServiceW")]
    private static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, string[]? lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "DeleteService")]
    private static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    private static bool ValidateDriverHash(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(path);
            byte[] hash = sha.ComputeHash(stream);
            string hashStr = Convert.ToHexString(hash);
            return string.Equals(hashStr, ExpectedDriverSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void SecureDirectory(string dirPath)
    {
        try
        {
            var dirInfo = new DirectoryInfo(dirPath);
            var sec = new DirectorySecurity();
            sec.SetAccessRuleProtection(true, false);

            var admins = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null);
            var users = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null);

            sec.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));

            dirInfo.SetAccessControl(sec);
        }
        catch (Exception ex)
        {
            App.LogDebug($"SecureDirectory notice: {ex.Message}");
        }
    }

    public static bool IsDriverOpen()
    {
        try
        {
            using var handle = CreateFile(DriverDevice, GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            return !handle.IsInvalid;
        }
        catch
        {
            return false;
        }
    }

    public static void EnsureDriverServiceRunning()
    {
        try
        {
            if (IsDriverOpen())
            {
                App.LogDebug("Driver device \\\\.\\WinRing0_1_2_0 is already open and ready.");
                return;
            }

            // 1. Extract WinRing0x64.sys to CommonApplicationData (pure ASCII C:\ProgramData\MiniHardwareWidget\...)
            string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MiniHardwareWidget");
            Directory.CreateDirectory(targetDir);
            SecureDirectory(targetDir);
            string targetSys = Path.Combine(targetDir, "WinRing0x64.sys");

            if (!File.Exists(targetSys) || !ValidateDriverHash(targetSys))
            {
                try { if (File.Exists(targetSys)) File.Delete(targetSys); } catch { }

                var asm = typeof(LibreHardwareMonitor.Hardware.Computer).Assembly;
                using var stream = asm.GetManifestResourceStream("LibreHardwareMonitor.Resources.WinRing0x64.gz");
                if (stream != null)
                {
                    using var fileStream = File.Create(targetSys);
                    stream.Position = 1;
                    using var gz = new GZipStream(stream, CompressionMode.Decompress);
                    gz.CopyTo(fileStream);
                    fileStream.Flush();
                    App.LogDebug($"Driver extracted to {targetSys} ({new FileInfo(targetSys).Length} bytes)");
                }
            }

            // DevSecOps Hardening: Verify cryptographic hash of the driver binary before execution
            if (!ValidateDriverHash(targetSys))
            {
                App.LogDebug("CRITICAL: Driver SHA-256 hash mismatch! Driver execution aborted for security.");
                return;
            }

            // 2. Register & start driver service using Unicode API
            string serviceName = "R0" + Process.GetCurrentProcess().ProcessName;
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == IntPtr.Zero)
            {
                App.LogDebug($"OpenSCManager failed: {Marshal.GetLastWin32Error()}");
                return;
            }

            try
            {
                IntPtr service = OpenService(scm, serviceName, SERVICE_ALL_ACCESS);
                if (service != IntPtr.Zero)
                {
                    // Existing service found. Try starting it.
                    if (!StartService(service, 0, null))
                    {
                        int err = Marshal.GetLastWin32Error();
                        App.LogDebug($"Existing service StartService returned: 0x{err:X}, deleting to recreate with safe ASCII path...");
                        DeleteService(service);
                        CloseServiceHandle(service);
                        service = IntPtr.Zero;
                    }
                }

                if (service == IntPtr.Zero)
                {
                    service = CreateService(
                        scm,
                        serviceName,
                        serviceName,
                        SERVICE_ALL_ACCESS,
                        SERVICE_KERNEL_DRIVER,
                        SERVICE_DEMAND_START,
                        SERVICE_ERROR_NORMAL,
                        targetSys,
                        null,
                        IntPtr.Zero,
                        null,
                        null,
                        null);

                    if (service != IntPtr.Zero)
                    {
                        App.LogDebug($"CreateServiceW created '{serviceName}' with path '{targetSys}'");
                        if (!StartService(service, 0, null))
                        {
                            App.LogDebug($"StartService returned error: 0x{Marshal.GetLastWin32Error():X}");
                        }
                    }
                    else
                    {
                        int err = Marshal.GetLastWin32Error();
                        App.LogDebug($"CreateServiceW failed: 0x{err:X}");
                    }
                }

                if (service != IntPtr.Zero)
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }

            // 3. Set Device Access Control (SDDL) so current session can interact with driver
            try
            {
                var fileInfo = new FileInfo(DriverDevice);
                var security = fileInfo.GetAccessControl();
                security.SetSecurityDescriptorSddlForm("O:BAG:SYD:(A;;FA;;;SY)(A;;FA;;;BA)");
                fileInfo.SetAccessControl(security);
            }
            catch { }

            App.LogDebug($"EnsureDriverServiceRunning finished. Driver open: {IsDriverOpen()}");
        }
        catch (Exception ex)
        {
            App.LogDebug($"EnsureDriverServiceRunning exception: {ex}");
        }
    }
}
