using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace RDPManager.Services;

/// <summary>
/// Writes short-lived "TERMSRV/&lt;host&gt;" credentials to Windows Credential Manager so that mstsc
/// can log on without prompting. Credentials written by this class carry a marker comment so that
/// leftovers (e.g. after a crash) can be recognised and removed.
/// </summary>
public static class CredentialInjector
{
    private const string Marker = "RDPManager temporary credential";
    private const int CRED_TYPE_DOMAIN_PASSWORD = 2;
    private const int CRED_PERSIST_SESSION = 1;
    private const int ERROR_NOT_FOUND = 1168;

    private static readonly Dictionary<string, int> RefCounts = new(StringComparer.OrdinalIgnoreCase);

    public enum State
    {
        None,
        Ours,
        Foreign,
    }

    public static string TargetForHost(string host) => "TERMSRV/" + host;

    public static State GetState(string target)
    {
        if (!CredRead(target, CRED_TYPE_DOMAIN_PASSWORD, 0, out var ptr))
            return State.None;
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            return cred.Comment == Marker ? State.Ours : State.Foreign;
        }
        finally
        {
            CredFree(ptr);
        }
    }

    /// <summary>Writes the credential and increments its reference count.</summary>
    public static void Write(string target, string userName, string password)
    {
        var blob = Encoding.Unicode.GetBytes(password);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_DOMAIN_PASSWORD,
                TargetName = target,
                Comment = Marker,
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_SESSION,
                UserName = userName,
            };
            if (!CredWrite(ref cred, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Nelze zapsat pověření pro {target}.");
            lock (RefCounts)
                RefCounts[target] = RefCounts.GetValueOrDefault(target) + 1;
        }
        finally
        {
            Array.Clear(blob);
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    /// <summary>Decrements the reference count and deletes the credential once nobody needs it.</summary>
    public static void Release(string target)
    {
        lock (RefCounts)
        {
            var count = RefCounts.GetValueOrDefault(target) - 1;
            if (count > 0)
            {
                RefCounts[target] = count;
                return;
            }
            RefCounts.Remove(target);
        }
        Delete(target);
    }

    public static void Delete(string target)
    {
        if (!CredDelete(target, CRED_TYPE_DOMAIN_PASSWORD, 0) && Marshal.GetLastWin32Error() != ERROR_NOT_FOUND)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>Removes credentials left behind by a previous run.</summary>
    public static void CleanupLeftovers()
    {
        if (!CredEnumerate(null, 0, out var count, out var list))
            return;
        var targets = new List<string>();
        try
        {
            for (var i = 0; i < count; i++)
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(Marshal.ReadIntPtr(list, i * IntPtr.Size));
                if (cred.Type == CRED_TYPE_DOMAIN_PASSWORD && cred.Comment == Marker && cred.TargetName != null)
                    targets.Add(cred.TargetName);
            }
        }
        finally
        {
            CredFree(list);
        }
        foreach (var t in targets)
            Delete(t);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string? TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredEnumerateW")]
    private static extern bool CredEnumerate(string? filter, int flags, out int count, out IntPtr credentials);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
