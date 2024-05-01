using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ExdGenerator;

internal unsafe sealed class AdditionalFileStream : Stream
{
    public string Path { get; }

    private SafeFileHandle Handle { get; }
    private bool Disposed { get; set; }
    private long position;

    public override long Position
    {
        get => position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Length
    {
        get
        {
            if (Disposed)
                throw new ObjectDisposedException(nameof(AdditionalFileStream));

            if (!GetFileSizeEx(Handle, out var length))
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

            return length;
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;

    public AdditionalFileStream(string path)
    {
        Path = path;
        position = 0;
        Handle = CreateFile(path, GENERIC_READ, FILE_SHARE_READ, 0, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, 0);

        if (Handle.IsInvalid)
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
    }

    public static AdditionalFileStream? TryOpen(string path)
    {
        try
        {
            return new AdditionalFileStream(path);
        }
        catch
        {
            return null;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (Disposed)
            throw new ObjectDisposedException(nameof(AdditionalFileStream));

        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));

        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (buffer.Length - offset < count)
            throw new ArgumentException("Invalid offset length.");

        fixed (byte* p = buffer)
        {
            if (!ReadFile(Handle, p + offset, count, out var read, 0))
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

            position += read;
            return read;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        if (Disposed)
            throw new ObjectDisposedException(nameof(AdditionalFileStream));

        var lo = (int)offset;
        var hi = (int)(offset >> 32);
        lo = SetFilePointer(Handle, lo, &hi, (int)origin);

        if (lo == -1)
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

        return position = (long)(((ulong)(uint)hi) << 32) | ((uint)lo);
    }

    public override void Flush()
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (!Disposed)
        {
            if (!Handle.IsInvalid && !Handle.IsClosed)
            {
                CloseHandle(Handle);
                Handle.SetHandleAsInvalid();
                Handle.Dispose();
            }

            Disposed = true;
        }

        base.Dispose(disposing);
    }

    #region P/Invoke Declarations

    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_BEGIN = 0;
    private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        byte* lpBuffer,
        int nNumberOfBytesToRead,
        out int lpNumberOfBytesRead,
        nint lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(SafeFileHandle hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int SetFilePointer(
        SafeFileHandle hFile,
        int lo, int* hi, int origin);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileSizeEx(SafeFileHandle hFile, out long lpFileSize);

    #endregion
}
