using System.Security.AccessControl;

namespace RecMode.Core.Infrastructure;

/// <summary>
/// Detects the one class of local-attacker exposure the portable-first design (§3.5) doesn't otherwise guard
/// against: RecMode never restricts the ACL on its own app/data folders, only checks that they're *writable*
/// (<see cref="IAppPaths"/>/<see cref="AppPaths.IsDataDirectoryWritable"/>). A folder created directly off a
/// drive root (not inside the current user's profile) inherits Windows' default ACL for that location, which
/// on a typical machine grants <c>Authenticated Users: Modify</c> — verified empirically on this box. That
/// means every other local account can both READ every recording/screenshot/settings file RecMode writes
/// there, and WRITE into the folder, including replacing the app's own bundled DLLs (a self-contained publish
/// ships its full managed runtime next to the exe). This class only detects and reports that condition — it
/// deliberately does not rewrite the ACL itself, since a folder this process doesn't own could leave the
/// rewrite half-applied or lock the current user out of their own data, which is a worse outcome than the
/// exposure it's trying to close.
/// </summary>
public static class FolderAclCheck
{
    // Well-known SIDs for the broad, "basically everyone" groups whose presence with write access means the
    // folder was never actually restricted to the current user — as opposed to a deliberately shared folder
    // ACL'd to a specific small group, which this check has no way to distinguish from a legitimate multi-
    // user setup and correctly wouldn't flag (that's a user/administrator choice, not a RecMode default).
    private const string EveryoneSid = "S-1-1-0";
    private const string AuthenticatedUsersSid = "S-1-5-11";
    private const string BuiltinUsersSid = "S-1-5-32-545";

    private const FileSystemRights WriteRights =
        FileSystemRights.Write | FileSystemRights.Modify | FileSystemRights.FullControl |
        FileSystemRights.CreateFiles | FileSystemRights.WriteData;

    /// <summary>True if a broad, non-owner-specific group (Everyone/Authenticated Users/Users — the ones a
    /// folder inherits by default from a location like a drive root) can write to <paramref name="path"/>.
    /// Best-effort: any failure inspecting the ACL (unsupported filesystem, permission denied reading the
    /// ACL itself) reports false rather than blocking startup over a diagnostic that couldn't run.</summary>
    public static bool GrantsWriteToBroadGroups(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            var info = new DirectoryInfo(path);
            DirectorySecurity acl = info.GetAccessControl();
            AuthorizationRuleCollection rules = acl.GetAccessRules(
                includeExplicit: true, includeInherited: true, targetType: typeof(System.Security.Principal.SecurityIdentifier));

            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.AccessControlType != AccessControlType.Allow)
                {
                    continue;
                }

                if ((rule.FileSystemRights & WriteRights) == 0)
                {
                    continue;
                }

                string sid = rule.IdentityReference.Value;
                if (sid is EveryoneSid or AuthenticatedUsersSid or BuiltinUsersSid)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or PlatformNotSupportedException or System.Security.SecurityException)
        {
            // Best-effort diagnostic — see class doc comment.
        }

        return false;
    }
}
