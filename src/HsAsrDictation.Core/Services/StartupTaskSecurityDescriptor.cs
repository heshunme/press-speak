using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace HsAsrDictation.Services;

internal static class StartupTaskSecurityDescriptor
{
    private const int FileAllAccess = 0x001F01FF;
    private const int FileGenericRead = 0x00120089;

    public static string Build(string userSid)
    {
        if (string.IsNullOrWhiteSpace(userSid))
        {
            throw new ArgumentException("当前用户 SID 不能为空。", nameof(userSid));
        }

        return $"O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;{userSid.Trim()})";
    }

    [SupportedOSPlatform("windows")]
    public static bool IsProtectedForUser(string? securityDescriptor, string userSid)
    {
        if (string.IsNullOrWhiteSpace(securityDescriptor))
        {
            return false;
        }

        try
        {
            var administratorsSid = new SecurityIdentifier(
                WellKnownSidType.BuiltinAdministratorsSid,
                domainSid: null);
            var localSystemSid = new SecurityIdentifier(
                WellKnownSidType.LocalSystemSid,
                domainSid: null);
            var targetUserSid = new SecurityIdentifier(userSid);
            var descriptor = new RawSecurityDescriptor(securityDescriptor);
            if (descriptor.Owner is null ||
                !descriptor.Owner.Equals(administratorsSid) ||
                descriptor.Group is null ||
                !descriptor.Group.Equals(administratorsSid) ||
                (descriptor.ControlFlags & ControlFlags.DiscretionaryAclPresent) == 0 ||
                (descriptor.ControlFlags & ControlFlags.DiscretionaryAclProtected) == 0 ||
                descriptor.DiscretionaryAcl is null ||
                descriptor.DiscretionaryAcl.Count != 3)
            {
                return false;
            }

            var systemAceFound = false;
            var administratorsAceFound = false;
            var userAceFound = false;

            foreach (GenericAce genericAce in descriptor.DiscretionaryAcl)
            {
                if (genericAce is not CommonAce
                    {
                        AceQualifier: AceQualifier.AccessAllowed,
                        AceFlags: AceFlags.None
                    } ace)
                {
                    return false;
                }

                if (ace.SecurityIdentifier.Equals(localSystemSid) &&
                    ace.AccessMask == FileAllAccess &&
                    !systemAceFound)
                {
                    systemAceFound = true;
                    continue;
                }

                if (ace.SecurityIdentifier.Equals(administratorsSid) &&
                    ace.AccessMask == FileAllAccess &&
                    !administratorsAceFound)
                {
                    administratorsAceFound = true;
                    continue;
                }

                if (ace.SecurityIdentifier.Equals(targetUserSid) &&
                    ace.AccessMask == FileGenericRead &&
                    !userAceFound)
                {
                    userAceFound = true;
                    continue;
                }

                return false;
            }

            return systemAceFound && administratorsAceFound && userAceFound;
        }
        catch
        {
            return false;
        }
    }
}
