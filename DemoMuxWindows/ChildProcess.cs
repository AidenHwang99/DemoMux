using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DemoMux;

internal sealed class ChildProcess : IDisposable
{
    private const uint KillOnJobClose = 0x2000;
    private const int JobExtendedLimitInformation = 9;
    private const int HandleListAttribute = 0x20002;
    private const int JobListAttribute = 0x2000D;
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint UseStandardHandles = 0x100;

    private readonly SafeFileHandle job;
    private readonly Process process;
    private readonly AnonymousPipeServerStream stdout;
    private readonly StringBuilder captured = new();
    private readonly Task drain;

    public int Id => process.Id;
    public bool Running => !process.HasExited;
    public int ExitCode => process.ExitCode;
    public string Output { get { lock (captured) return captured.ToString(); } }

    private ChildProcess(SafeFileHandle job, Process process, AnonymousPipeServerStream stdout)
    {
        this.job = job;
        this.process = process;
        this.stdout = stdout;
        drain = CaptureAsync();
    }

    private async Task CaptureAsync()
    {
        try
        {
            using var reader = new StreamReader(stdout, Encoding.UTF8);
            var buffer = new char[4096];
            int count;
            while ((count = await reader.ReadAsync(buffer)) > 0)
            {
                lock (captured)
                {
                    captured.Append(buffer, 0, count);
                    if (captured.Length > 65536) captured.Remove(0, captured.Length - 65536);
                }
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public async Task WaitAsync(CancellationToken cancellation)
    {
        await process.WaitForExitAsync(cancellation);
        await drain.WaitAsync(cancellation);
    }

    public void Dispose()
    {
        job.Dispose();
        process.WaitForExit(2000);
        stdout.Dispose();
        process.Dispose();
    }

    public static ChildProcess Start(string executable, params string[] arguments)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        var output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        IntPtr attributes = IntPtr.Zero;
        IntPtr handles = IntPtr.Zero;
        bool attributesInitialized = false;
        ProcessInformation info = default;
        Process? child = null;
        try
        {
            Check(!job.IsInvalid);
            var limits = new JobLimits { Basic = new BasicLimits { Flags = KillOnJobClose } };
            Check(SetInformationJobObject(job, JobExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<JobLimits>()));

            // Only the child's stdin and output pipe handles cross the process boundary.
            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref size);
            if (size == IntPtr.Zero) throw new Win32Exception();
            attributes = Marshal.AllocHGlobal(size);
            Check(InitializeProcThreadAttributeList(attributes, 2, 0, ref size));
            attributesInitialized = true;
            handles = Marshal.AllocHGlobal(3 * IntPtr.Size);
            Marshal.WriteIntPtr(handles, 0, input.ClientSafePipeHandle.DangerousGetHandle());
            Marshal.WriteIntPtr(handles, IntPtr.Size, output.ClientSafePipeHandle.DangerousGetHandle());
            Check(UpdateProcThreadAttribute(attributes, 0, new IntPtr(HandleListAttribute),
                handles, new IntPtr(2 * IntPtr.Size), IntPtr.Zero, IntPtr.Zero));
            // Add the child to its job atomically as Windows creates the process.
            Marshal.WriteIntPtr(handles, 2 * IntPtr.Size, job.DangerousGetHandle());
            Check(UpdateProcThreadAttribute(attributes, 0, new IntPtr(JobListAttribute),
                IntPtr.Add(handles, 2 * IntPtr.Size), new IntPtr(IntPtr.Size), IntPtr.Zero, IntPtr.Zero));

            var startup = new StartupInfoEx
            {
                Startup = new StartupInfo
                {
                    Size = (uint)Marshal.SizeOf<StartupInfoEx>(),
                    Flags = UseStandardHandles,
                    StdIn = input.ClientSafePipeHandle.DangerousGetHandle(),
                    StdOut = output.ClientSafePipeHandle.DangerousGetHandle(),
                    StdErr = output.ClientSafePipeHandle.DangerousGetHandle()
                },
                Attributes = attributes
            };
            var line = new StringBuilder(string.Join(" ", new[] { executable }.Concat(arguments).Select(Quote)));
            const uint flags = CreateSuspended | CreateNoWindow | ExtendedStartupInfoPresent;
            Check(CreateProcess(executable, line, IntPtr.Zero, IntPtr.Zero, true,
                flags, IntPtr.Zero, Path.GetDirectoryName(executable), ref startup, out info));
            child = Process.GetProcessById((int)info.ProcessId);
            _ = child.Handle;
            output.DisposeLocalCopyOfClientHandle();
            input.DisposeLocalCopyOfClientHandle();
            Check(ResumeThread(info.Thread) != uint.MaxValue);
            return new ChildProcess(job, child, output);
        }
        catch
        {
            if (info.Process != IntPtr.Zero) TerminateProcess(info.Process, 1);
            job.Dispose();
            output.Dispose();
            child?.Dispose();
            throw;
        }
        finally
        {
            if (info.Thread != IntPtr.Zero) CloseHandle(info.Thread);
            if (info.Process != IntPtr.Zero) CloseHandle(info.Process);
            if (attributesInitialized) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
        }
    }

    internal static string Quote(string argument)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static void Check(bool result)
    {
        if (!result) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public UIntPtr MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public string? Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedBytes, StdIn, StdOut, StdErr;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo Startup;
        public IntPtr Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref JobLimits limits, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr attributes, int count, uint flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr attributes, uint flags, IntPtr attribute,
        IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributes);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(string application, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags,
        IntPtr environment, string? directory, ref StartupInfoEx startup, out ProcessInformation info);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")]
    private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
