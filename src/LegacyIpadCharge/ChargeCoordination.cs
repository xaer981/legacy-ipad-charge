using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace LegacyIpadCharge;

internal interface IChargeCoordination
{
    IDisposable Lock();
    IDisposable LockForRestore(CancellationToken token);
    IChargeTrialSignal BeginTrial();
}

internal interface IChargeTrialSignal : IDisposable
{
    bool Requested { get; }
    void Wait(TimeSpan duration, CancellationToken token);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsChargeCoordination(string lockPath, string restoreEvent) : IChargeCoordination
{
    public IDisposable Lock() => LibusbFiles.Lock(lockPath);

    public IChargeTrialSignal BeginTrial()
    {
        var signal = new EventWaitHandle(false, EventResetMode.ManualReset, restoreEvent, out var created);
        if (created)
            return new TrialSignal(signal);

        signal.Dispose();
        throw new IOException("A previous restore is still finishing. Retry the trial.");
    }

    public IDisposable LockForRestore(CancellationToken token)
    {
        var wait = Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return Lock(); }
            catch (IOException error) when ((error.HResult & 0xFFFF) is 32 or 33)
            {
                if (wait.Elapsed >= TimeSpan.FromSeconds(15))
                    throw new IOException("Another operation is still active. Retry the restore or reconnect the iPad.", error);
                if (EventWaitHandle.TryOpenExisting(restoreEvent, out var activeTrial))
                {
                    using (activeTrial)
                        activeTrial.Set();
                }
                token.WaitHandle.WaitOne(50);
            }
        }
    }

    private sealed class TrialSignal(EventWaitHandle signal) : IChargeTrialSignal
    {
        public bool Requested => signal.WaitOne(0);
        public void Wait(TimeSpan duration, CancellationToken token) =>
            WaitHandle.WaitAny([signal, token.WaitHandle], duration);
        public void Dispose() => signal.Dispose();
    }
}

[SupportedOSPlatform("linux")]
internal sealed class LinuxChargeCoordination(string directory) : IChargeCoordination
{
    private const int Exclusive = 2;
    private const int NonBlocking = 4;
    private readonly string restoration = Path.Combine(directory, "restore-request");
    private readonly string lockPath = Path.Combine(directory, "charge.lock");

    public IDisposable Lock()
    {
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        // All callers must lock the same inode, even across a process exit.
        var handle = File.OpenHandle(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        if (flock(handle, Exclusive | NonBlocking) == 0)
            return handle;

        var error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        throw new IOException($"Cannot lock the Linux charging state (errno {error}).", error);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int flock(SafeFileHandle fd, int operation);

    public IChargeTrialSignal BeginTrial()
    {
        File.Delete(restoration);
        return new TrialSignal(restoration);
    }

    public IDisposable LockForRestore(CancellationToken token)
    {
        var wait = Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return Lock(); }
            catch (IOException error) when ((error.HResult & 0xFFFF) is 11 or 32)
            {
                if (wait.Elapsed >= TimeSpan.FromSeconds(15))
                    throw new IOException("Another operation is still active. Retry the restore or reconnect the iPad.", error);
                File.WriteAllText(restoration, "");
                token.WaitHandle.WaitOne(50);
            }
        }
    }

    private sealed class TrialSignal(string restoration) : IChargeTrialSignal
    {
        public bool Requested => File.Exists(restoration);

        public void Wait(TimeSpan duration, CancellationToken token)
        {
            var elapsed = Stopwatch.StartNew();
            while (!Requested && !token.IsCancellationRequested)
            {
                var remaining = duration - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    return;
                token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(Math.Min(50, remaining.TotalMilliseconds)));
            }
        }

        public void Dispose() => File.Delete(restoration);
    }
}
