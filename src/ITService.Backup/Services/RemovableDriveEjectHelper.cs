using System.ComponentModel;
using System.Runtime.InteropServices;
using ITService.Backup.Helpers;
using Microsoft.Win32.SafeHandles;

namespace ITService.Backup.Services;

internal static class RemovableDriveEjectHelper
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint IoctlStorageEjectMedia = 0x2D4808;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    public static bool TryEject(string? driveLetter, out string errorMessage)
    {
        var letter = DriveLetterHelper.Normalize(driveLetter);
        if (string.IsNullOrEmpty(letter))
        {
            errorMessage = "Буква диска не указана.";
            return false;
        }

        var drive = DriveLetterHelper.TryGetDrive(letter, requireRemovable: false);
        if (drive is not { IsReady: true })
        {
            errorMessage = $"Диск {letter}: не подключён или недоступен.";
            return false;
        }

        if (drive.DriveType != DriveType.Removable)
        {
            errorMessage = $"Диск {letter}: безопасное извлечение доступно только для съёмных накопителей.";
            return false;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();

        var devicePath = $"\\\\.\\{letter}:";
        using var handle = CreateFile(
            devicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
            return Fail(letter, Marshal.GetLastWin32Error(), out errorMessage);

        if (!DeviceIoControl(handle, IoctlStorageEjectMedia, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            return Fail(letter, Marshal.GetLastWin32Error(), out errorMessage);

        errorMessage = "";
        return true;
    }

    private static bool Fail(string letter, int win32Error, out string errorMessage)
    {
        errorMessage = win32Error switch
        {
            0 => $"Не удалось извлечь диск {letter}:.",
            5 => $"Диск {letter}: отказано в доступе. Запустите программу от имени администратора.",
            21 or 109 or 170 => $"Диск {letter}: используется другой программой. Закройте файлы на накопителе и попробуйте снова.",
            _ => $"Не удалось извлечь диск {letter}: ({win32Error}). {new Win32Exception(win32Error).Message}"
        };
        return false;
    }
}
