using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

// WScript.Shell returns ANSI paths on some English Windows images. Explorer uses
// IShellLinkW; inspect the same Unicode interface so Chinese paths are not lost.
public static class DemoMuxShortcutReader
{
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, IntPtr findData, uint flags);
    }

    public static string Target(string shortcut)
    {
        var link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;
        try
        {
            ((IPersistFile)link).Load(shortcut, 0);
            var buffer = new StringBuilder(32768);
            ((IShellLinkW)link).GetPath(buffer, buffer.Capacity, IntPtr.Zero, 4);
            return buffer.ToString();
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }
}
