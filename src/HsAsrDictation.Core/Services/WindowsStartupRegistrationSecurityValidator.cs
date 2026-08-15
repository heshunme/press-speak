using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HsAsrDictation.Services;

[SupportedOSPlatform("windows")]
internal static class WindowsStartupRegistrationSecurityValidator
{
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenQuery = 0x0008;
    private const uint ReadControl = 0x00020000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint MaximumAllowed = 0x02000000;
    private const uint GenericAll = 0x10000000;
    private const uint GenericWrite = 0x40000000;
    private const int ErrorInsufficientBuffer = 122;
    private const uint SecurityGroupEnabled = 0x00000004;
    private const int MaxDosDeviceTargetLength = 32768;
    // 以下三个 SID 是修复流程（WindowsStartupAclRepairService）唯一的信任主体来源；
    // 修复授予的账户必须和这里的判定完全一致，否则会出现"修复后仍然校验不过"或
    // "修复授予了校验并不认可的信任范围"的漂移，因此保持 internal 而非各自重复硬编码。
    internal const string LocalSystemSid = "S-1-5-18";
    internal const string AdministratorsSid = "S-1-5-32-544";
    private const string CreatorOwnerSid = "S-1-3-0";
    internal const string TrustedInstallerSid =
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    private const uint FileWriteData = 0x00000002;
    private const uint FileAppendData = 0x00000004;
    private const uint FileWriteExtendedAttributes = 0x00000010;
    private const uint FileDeleteChild = 0x00000040;
    private const uint FileWriteAttributes = 0x00000100;
    private const uint Delete = 0x00010000;
    private const uint WriteDac = 0x00040000;
    private const uint WriteOwner = 0x00080000;

    private const uint FileMutationMask =
        FileWriteData |
        FileAppendData |
        FileWriteExtendedAttributes |
        FileWriteAttributes |
        Delete |
        WriteDac |
        WriteOwner;

    private const uint DirectoryMutationMask =
        FileWriteData |
        FileAppendData |
        FileWriteExtendedAttributes |
        FileDeleteChild |
        FileWriteAttributes |
        Delete |
        WriteDac |
        WriteOwner;

    // 与 Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories) 等价的遍历语义
    // （递归全部子目录、不可访问时抛错由调用方兜底），额外跳过重解析点以免顺着
    // 目录联接点/符号链接递归到安装树之外。
    private static readonly EnumerationOptions HardLinkScanEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static StartupRegistrationChangeResult Validate(
        string executablePath,
        string targetUserSid)
    {
        var outcome = ValidateCore(executablePath, targetUserSid);
        return outcome.WasSuccessful
            ? StartupRegistrationChangeResult.Succeeded()
            : StartupRegistrationChangeResult.Failed(outcome.Message!);
    }

    /// <summary>
    /// 供 ACL 修复流程判断"是否值得为这次失败发起一次单独的 icacls 提权修复"。
    /// 唯一有权判定"可以提权"的仍然是 <see cref="Validate"/>（经由本方法内部调用的
    /// <see cref="ValidateCore"/> 完全相同的逻辑）；这里只是在同一次校验结果之上
    /// 追加"失败原因是否属于可修复的应用目录 ACL/属主问题"的分类判断，绝不放宽
    /// 或替代原有校验。修复流程必须在执行修复后重新调用 <see cref="Validate"/>
    /// 得到全新结果，不能复用这里返回的布尔值作为"已修复"的证明。
    /// </summary>
    internal static bool IsRepairableAclFailure(string executablePath, string targetUserSid)
    {
        var outcome = ValidateCore(executablePath, targetUserSid);
        if (outcome.Category != ValidationFailureCategory.ApplicationTreeAcl)
        {
            return false;
        }

        // 硬链接不带重解析点属性，现有树遍历不会拦截；但属主/DACL 挂在共享的文件
        // 记录上而非目录项上，递归修复会连带改到硬链接指向的、安装目录之外的文件，
        // 因此发现硬链接一律按"不可自动修复"处理（修复范围之外的场景，交由人工处理）。
        var applicationDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        return applicationDirectory is not null && !HasHardLink(applicationDirectory, out _);
    }

    /// <summary>
    /// 扫描目录树内是否存在硬链接（<c>nNumberOfLinks &gt; 1</c>）。修复前用于判断
    /// 是否可以自动修复，修复后用于兜底复查——两处必须调用同一份逻辑，不能各自实现。
    /// </summary>
    internal static bool HasHardLink(string directory, out string? offendingPath)
    {
        try
        {
            // 与 SearchOption.AllDirectories 语义保持一致（递归、不可访问时抛错走兜底），
            // 额外跳过重解析点：既不跟随目录联接点/符号链接递归出安装树，也不把
            // 链接本身的文件记录当作硬链接检测目标。
            foreach (var entry in Directory.EnumerateFiles(directory, "*", HardLinkScanEnumerationOptions))
            {
                if (GetHardLinkCount(entry) > 1)
                {
                    offendingPath = entry;
                    return true;
                }
            }

            offendingPath = null;
            return false;
        }
        catch (Exception)
        {
            // 无法枚举/打开时保守地按"存在风险"处理。
            offendingPath = directory;
            return true;
        }
    }

    private static uint GetHardLinkCount(string filePath)
    {
        using var handle = CreateFileW(
            filePath,
            ReadControl,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法打开硬链接检测目标：{filePath}");
        }

        if (!GetFileInformationByHandle(handle, out var information))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法读取文件信息：{filePath}");
        }

        return information.NumberOfLinks;
    }

    private static ValidationOutcome ValidateCore(
        string executablePath,
        string targetUserSid)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ValidationOutcome.Failure("管理员登录自启动仅支持 Windows。", ValidationFailureCategory.Other);
        }

        try
        {
            var normalizedExecutablePath = Path.GetFullPath(executablePath);
            if (!File.Exists(normalizedExecutablePath))
            {
                return ValidationOutcome.Failure(
                    "当前程序文件不存在，无法配置管理员登录自启动。",
                    ValidationFailureCategory.Other);
            }

            using var identity = WindowsIdentity.GetCurrent();
            var targetSid = new SecurityIdentifier(targetUserSid);
            if (identity.User is null || !identity.User.Equals(targetSid))
            {
                return ValidationOutcome.Failure(
                    "管理员登录自启动只能由目标 Windows 账号本人配置；不支持使用其他管理员账号凭据代为创建。",
                    ValidationFailureCategory.Other);
            }

            using var currentToken = OpenCurrentProcessToken();
            var currentElevationType = GetElevationType(currentToken);
            if (currentElevationType == TokenElevationType.Default)
            {
                var currentPrincipal = new WindowsPrincipal(identity);
                return ValidationOutcome.Failure(
                    currentPrincipal.IsInRole(WindowsBuiltInRole.Administrator)
                        ? "当前系统没有可用于安全校验的 UAC 非提升令牌；请改用普通模式自动启动。"
                        : "当前 Windows 账号不属于管理员组，无法配置管理员模式自动启动。",
                    ValidationFailureCategory.Other);
            }

            using var linkedToken = GetLinkedToken(currentToken);
            var linkedElevationType = GetElevationType(linkedToken);
            if (!IsExpectedLinkedTokenPair(currentElevationType, linkedElevationType))
            {
                return ValidationOutcome.Failure(
                    "Windows UAC 令牌关系异常，无法安全配置管理员登录自启动。",
                    ValidationFailureCategory.Other);
            }

            var membershipSource = currentElevationType == TokenElevationType.Full
                ? currentToken
                : linkedToken;
            var standardSource = currentElevationType == TokenElevationType.Limited
                ? currentToken
                : linkedToken;

            if (!IsAdministrator(membershipSource))
            {
                return ValidationOutcome.Failure(
                    "当前 Windows 账号不属于管理员组，无法配置管理员模式自动启动。",
                    ValidationFailureCategory.Other);
            }

            using var standardToken = DuplicateAsIdentificationToken(standardSource);
            return ValidateProtectedApplicationTree(normalizedExecutablePath, standardToken);
        }
        catch (Exception ex)
        {
            return ValidationOutcome.Failure(
                $"无法验证管理员登录自启动的安全条件：{ex.Message}",
                ValidationFailureCategory.Other);
        }
    }

    private static ValidationOutcome ValidateProtectedApplicationTree(
        string executablePath,
        SafeAccessTokenHandle standardToken)
    {
        var applicationDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(applicationDirectory))
        {
            return ValidationOutcome.Failure("无法解析当前程序目录。", ValidationFailureCategory.Other);
        }

        var rootPath = Path.GetPathRoot(applicationDirectory);
        if (string.IsNullOrWhiteSpace(rootPath) || applicationDirectory.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return ValidationOutcome.Failure(
                "管理员模式自动启动不支持网络安装目录。",
                ValidationFailureCategory.UnsupportedVolumeOrMapping);
        }

        var rootMappingValidationError = ValidateApplicationRootMapping(rootPath);
        if (rootMappingValidationError is not null)
        {
            return ValidationOutcome.Failure(
                rootMappingValidationError,
                ValidationFailureCategory.UnsupportedVolumeOrMapping);
        }

        var drive = new DriveInfo(rootPath);
        var volumeValidationError = ValidateApplicationVolume(
            drive.DriveType,
            drive.DriveFormat);
        if (volumeValidationError is not null)
        {
            return ValidationOutcome.Failure(
                volumeValidationError,
                ValidationFailureCategory.UnsupportedVolumeOrMapping);
        }

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(applicationDirectory);

        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();
            var directoryOutcome = ValidateTreeEntry(
                directory,
                isDirectory: true,
                DirectoryMutationMask,
                standardToken);
            if (directoryOutcome is { } directoryFailure)
            {
                return directoryFailure;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return ValidationOutcome.Failure(
                        $"程序目录包含重解析点，无法安全配置管理员模式自动启动：{entry}",
                        ValidationFailureCategory.ReparsePoint);
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pendingDirectories.Push(entry);
                    continue;
                }

                var fileOutcome = ValidateTreeEntry(
                    entry,
                    isDirectory: false,
                    FileMutationMask,
                    standardToken);
                if (fileOutcome is { } fileFailure)
                {
                    return fileFailure;
                }
            }
        }

        var ancestorError = ValidateAncestorReplacementRisk(applicationDirectory, standardToken);
        return ancestorError is null
            ? ValidationOutcome.Success
            : ValidationOutcome.Failure(ancestorError, ValidationFailureCategory.AncestorReplacementRisk);
    }

    internal static string? ValidateApplicationVolume(DriveType driveType, string driveFormat)
    {
        if (driveType != DriveType.Fixed)
        {
            return "管理员模式自动启动要求程序安装在本机固定磁盘上，不支持网络盘或可移动卷。";
        }

        return string.Equals(driveFormat, "NTFS", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(driveFormat, "ReFS", StringComparison.OrdinalIgnoreCase)
            ? null
            : "管理员模式自动启动要求程序安装在支持 Windows ACL 的 NTFS 或 ReFS 卷上。";
    }

    internal static string? ValidateDosDeviceTarget(string rootPath, string deviceTarget)
    {
        if (rootPath.Length != 3 ||
            !char.IsAsciiLetter(rootPath[0]) ||
            rootPath[1] != ':' ||
            rootPath[2] is not ('\\' or '/'))
        {
            return "管理员模式自动启动只支持直接挂载的本机盘符路径。";
        }

        return deviceTarget.StartsWith(@"\Device\HarddiskVolume", StringComparison.OrdinalIgnoreCase)
            ? null
            : "管理员模式自动启动不支持 SUBST 或其他可重定向的 DOS 设备映射。";
    }

    private static string? ValidateApplicationRootMapping(string rootPath)
    {
        if (rootPath.Length != 3 || rootPath[1] != ':')
        {
            return ValidateDosDeviceTarget(rootPath, string.Empty);
        }

        var deviceName = rootPath[..2];
        var target = new StringBuilder(MaxDosDeviceTargetLength);
        if (QueryDosDeviceW(deviceName, target, target.Capacity) == 0)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"无法解析程序所在盘符的设备映射：{deviceName}");
        }

        return ValidateDosDeviceTarget(rootPath, target.ToString());
    }

    private static ValidationOutcome? ValidateTreeEntry(
        string path,
        bool isDirectory,
        uint mutationMask,
        SafeAccessTokenHandle standardToken)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return ValidationOutcome.Failure(
                $"程序路径包含重解析点，无法安全配置管理员模式自动启动：{path}",
                ValidationFailureCategory.ReparsePoint);
        }

        var access = InspectPathSecurity(path, isDirectory, mutationMask, standardToken);
        if (access.UntrustedMutationAccess != 0)
        {
            return ValidationOutcome.Failure(
                $"程序文件或目录允许非受信任主体写入，已拒绝管理员模式自动启动：{path}（{access.UntrustedPrincipal}）。",
                ValidationFailureCategory.ApplicationTreeAcl);
        }

        return (access.CurrentUserAccess & mutationMask) == 0
            ? null
            : ValidationOutcome.Failure(
                $"普通权限进程可以修改程序文件或目录，已拒绝管理员模式自动启动：{path}。请先安装到 ACL 受保护的目录。",
                ValidationFailureCategory.ApplicationTreeAcl);
    }

    private static string? ValidateAncestorReplacementRisk(
        string applicationDirectory,
        SafeAccessTokenHandle standardToken)
    {
        var childDirectory = new DirectoryInfo(applicationDirectory);
        var childAccess = InspectPathSecurity(
            childDirectory.FullName,
            isDirectory: true,
            DirectoryMutationMask,
            standardToken);

        while (childDirectory.Parent is { } parentDirectory)
        {
            var parentAttributes = File.GetAttributes(parentDirectory.FullName);
            if ((parentAttributes & FileAttributes.ReparsePoint) != 0)
            {
                return $"程序路径包含重解析点，无法安全配置管理员模式自动启动：{parentDirectory.FullName}";
            }

            var parentAccess = InspectPathSecurity(
                parentDirectory.FullName,
                isDirectory: true,
                DirectoryMutationMask,
                standardToken);
            if ((parentAccess.UntrustedMutationAccess & (WriteDac | WriteOwner)) != 0)
            {
                return $"程序路径上级目录的 ACL 可由非受信任主体修改，已拒绝管理员模式自动启动：{parentDirectory.FullName}（{parentAccess.UntrustedPrincipal}）。";
            }

            var canRemoveChild =
                (childAccess.CurrentUserAccess & Delete) != 0 ||
                (parentAccess.CurrentUserAccess & FileDeleteChild) != 0;
            var canCreateReplacement = (parentAccess.CurrentUserAccess & FileAppendData) != 0;
            if ((canRemoveChild && canCreateReplacement) ||
                (parentAccess.CurrentUserAccess & (WriteDac | WriteOwner)) != 0)
            {
                return $"普通权限进程可以替换程序路径组件，已拒绝管理员模式自动启动：{parentDirectory.FullName}。";
            }

            var untrustedCanRemoveChild =
                (childAccess.UntrustedMutationAccess & Delete) != 0 ||
                (parentAccess.UntrustedMutationAccess & FileDeleteChild) != 0;
            var untrustedCanCreateReplacement =
                (parentAccess.UntrustedMutationAccess & FileAppendData) != 0;
            if (untrustedCanRemoveChild && untrustedCanCreateReplacement)
            {
                return $"非受信任主体可以替换程序路径组件，已拒绝管理员模式自动启动：{parentDirectory.FullName}。";
            }

            childDirectory = parentDirectory;
            childAccess = parentAccess;
        }

        return null;
    }

    private static PathSecurityInspection InspectPathSecurity(
        string path,
        bool isDirectory,
        uint mutationMask,
        SafeAccessTokenHandle standardToken)
    {
        using var handle = CreateFileW(
            path,
            ReadControl,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint | (isDirectory ? FileFlagBackupSemantics : 0),
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法打开 ACL 校验目标：{path}");
        }

        var error = GetSecurityInfo(
            handle,
            SeObjectType.FileObject,
            SecurityInformation.Owner | SecurityInformation.Group | SecurityInformation.Dacl,
            out _,
            out _,
            out _,
            out _,
            out var securityDescriptor);
        if (error != 0)
        {
            throw new Win32Exception(unchecked((int)error), $"无法读取 ACL：{path}");
        }

        try
        {
            var descriptorInspection = InspectSecurityDescriptor(
                ReadSecurityDescriptor(securityDescriptor),
                mutationMask);
            return new PathSecurityInspection(
                AccessCheckMaximumAllowed(securityDescriptor, standardToken),
                descriptorInspection.UntrustedMutationAccess,
                descriptorInspection.UntrustedPrincipal);
        }
        finally
        {
            _ = LocalFree(securityDescriptor);
        }
    }

    internal static string? ValidateFileSystemSecurityDescriptor(
        string securityDescriptor,
        bool isDirectory)
    {
        var inspection = InspectSecurityDescriptor(
            new RawSecurityDescriptor(securityDescriptor),
            isDirectory ? DirectoryMutationMask : FileMutationMask);
        return inspection.UntrustedMutationAccess == 0
            ? null
            : inspection.UntrustedPrincipal ?? "未知非受信任主体";
    }

    private static RawSecurityDescriptor ReadSecurityDescriptor(IntPtr securityDescriptor)
    {
        var descriptorLength = GetSecurityDescriptorLength(securityDescriptor);
        if (descriptorLength == 0 || descriptorLength > int.MaxValue)
        {
            throw new InvalidOperationException("Windows 返回了无效的文件系统安全描述符。");
        }

        var descriptorBytes = new byte[descriptorLength];
        Marshal.Copy(securityDescriptor, descriptorBytes, 0, checked((int)descriptorLength));
        return new RawSecurityDescriptor(descriptorBytes, 0);
    }

    private static SecurityDescriptorInspection InspectSecurityDescriptor(
        RawSecurityDescriptor descriptor,
        uint mutationMask)
    {
        if (descriptor.Owner is null || !IsTrustedWriter(descriptor.Owner))
        {
            return new SecurityDescriptorInspection(
                WriteDac,
                descriptor.Owner?.Value ?? "缺少所有者");
        }

        if (descriptor.DiscretionaryAcl is null)
        {
            return new SecurityDescriptorInspection(mutationMask, "缺少 DACL");
        }

        uint untrustedMutationAccess = 0;
        string? untrustedPrincipal = null;
        foreach (GenericAce genericAce in descriptor.DiscretionaryAcl)
        {
            if (genericAce is not QualifiedAce ace)
            {
                return new SecurityDescriptorInspection(
                    mutationMask,
                    $"不支持的 ACE 类型 {genericAce.AceType}");
            }

            if (ace.AceQualifier != AceQualifier.AccessAllowed)
            {
                continue;
            }

            if (IsTrustedWriter(ace.SecurityIdentifier) ||
                (string.Equals(ace.SecurityIdentifier.Value, CreatorOwnerSid, StringComparison.Ordinal) &&
                 (ace.AceFlags & AceFlags.InheritOnly) != 0))
            {
                continue;
            }

            var mutationAccess = ExpandGenericAccess(unchecked((uint)ace.AccessMask)) & mutationMask;
            if (mutationAccess == 0)
            {
                continue;
            }

            untrustedMutationAccess |= mutationAccess;
            untrustedPrincipal ??= ace.SecurityIdentifier.Value;
        }

        return new SecurityDescriptorInspection(untrustedMutationAccess, untrustedPrincipal);
    }

    private static bool IsTrustedWriter(SecurityIdentifier sid)
        => string.Equals(sid.Value, LocalSystemSid, StringComparison.Ordinal) ||
           string.Equals(sid.Value, AdministratorsSid, StringComparison.Ordinal) ||
           string.Equals(sid.Value, TrustedInstallerSid, StringComparison.Ordinal);

    private static uint ExpandGenericAccess(uint accessMask)
    {
        if ((accessMask & GenericAll) != 0)
        {
            accessMask |= FileGenericMapping.GenericAll;
        }

        if ((accessMask & GenericWrite) != 0)
        {
            accessMask |= FileGenericMapping.GenericWrite;
        }

        return accessMask;
    }

    private static uint AccessCheckMaximumAllowed(
        IntPtr securityDescriptor,
        SafeAccessTokenHandle standardToken)
    {
        var mapping = FileGenericMapping;
        uint privilegeSetLength = 0;
        var firstCallSucceeded = AccessCheck(
            securityDescriptor,
            standardToken,
            MaximumAllowed,
            ref mapping,
            IntPtr.Zero,
            ref privilegeSetLength,
            out var grantedAccess,
            out var accessStatus);
        if (firstCallSucceeded)
        {
            return accessStatus ? grantedAccess : 0;
        }

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorInsufficientBuffer || privilegeSetLength == 0)
        {
            throw new Win32Exception(error, "Windows AccessCheck 失败。");
        }

        var privilegeSet = Marshal.AllocHGlobal(checked((int)privilegeSetLength));
        try
        {
            mapping = FileGenericMapping;
            if (!AccessCheck(
                    securityDescriptor,
                    standardToken,
                    MaximumAllowed,
                    ref mapping,
                    privilegeSet,
                    ref privilegeSetLength,
                    out grantedAccess,
                    out accessStatus))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows AccessCheck 失败。");
            }

            return accessStatus ? grantedAccess : 0;
        }
        finally
        {
            Marshal.FreeHGlobal(privilegeSet);
        }
    }

    private static SafeAccessTokenHandle OpenCurrentProcessToken()
    {
        if (!OpenProcessToken(
                GetCurrentProcess(),
                TokenQuery | TokenDuplicate,
                out var token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开当前 Windows 进程令牌。");
        }

        return token;
    }

    private static SafeAccessTokenHandle GetLinkedToken(SafeAccessTokenHandle token)
    {
        var buffer = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            if (!GetTokenInformation(
                    token,
                    TokenInformationClass.LinkedToken,
                    buffer,
                    (uint)IntPtr.Size,
                    out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 Windows UAC 关联令牌。");
            }

            var linkedToken = Marshal.ReadIntPtr(buffer);
            if (linkedToken == IntPtr.Zero)
            {
                throw new InvalidOperationException("Windows UAC 关联令牌为空。");
            }

            return new SafeAccessTokenHandle(linkedToken);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static TokenElevationType GetElevationType(SafeAccessTokenHandle token)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            if (!GetTokenInformation(
                    token,
                    TokenInformationClass.ElevationType,
                    buffer,
                    sizeof(int),
                    out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 Windows UAC 令牌类型。");
            }

            return (TokenElevationType)Marshal.ReadInt32(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static SafeAccessTokenHandle DuplicateAsIdentificationToken(SafeAccessTokenHandle token)
    {
        // AccessCheck 只需要 Identification 级的模拟令牌。提升进程经 TokenLinkedToken 取得的
        // 非提升令牌本身即 Identification 级（无 SeTcbPrivilege 时），请求更高的 Impersonation
        // 级会以 ERROR_BAD_IMPERSONATION_LEVEL 失败；统一用最低必要级别复制。
        if (!DuplicateToken(
                token,
                SecurityImpersonationLevel.Identification,
                out var duplicate))
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"无法复制 Windows 身份令牌（错误 {error}）。");
        }

        return duplicate;
    }

    private static bool IsAdministrator(SafeAccessTokenHandle token)
    {
        var administratorSid = new SecurityIdentifier(
            WellKnownSidType.BuiltinAdministratorsSid,
            domainSid: null);
        var sidBytes = new byte[administratorSid.BinaryLength];
        administratorSid.GetBinaryForm(sidBytes, 0);
        var sidPointer = Marshal.AllocHGlobal(sidBytes.Length);
        try
        {
            Marshal.Copy(sidBytes, 0, sidPointer, sidBytes.Length);
            var groupsBuffer = GetTokenInformationBuffer(token, TokenInformationClass.Groups);
            try
            {
                var groupCount = unchecked((uint)Marshal.ReadInt32(groupsBuffer));
                var firstGroupOffset = Marshal.OffsetOf<TokenGroupsHeader>(
                    nameof(TokenGroupsHeader.FirstGroup)).ToInt32();
                var groupSize = Marshal.SizeOf<SidAndAttributes>();
                for (var index = 0u; index < groupCount; index++)
                {
                    var groupPointer = IntPtr.Add(
                        groupsBuffer,
                        checked(firstGroupOffset + (int)index * groupSize));
                    var group = Marshal.PtrToStructure<SidAndAttributes>(groupPointer);
                    if ((group.Attributes & SecurityGroupEnabled) != 0 &&
                        EqualSid(group.Sid, sidPointer))
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(groupsBuffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(sidPointer);
        }
    }

    private static bool IsExpectedLinkedTokenPair(
        TokenElevationType current,
        TokenElevationType linked) =>
        (current == TokenElevationType.Limited && linked == TokenElevationType.Full) ||
        (current == TokenElevationType.Full && linked == TokenElevationType.Limited);

    private static IntPtr GetTokenInformationBuffer(
        SafeAccessTokenHandle token,
        TokenInformationClass informationClass)
    {
        _ = GetTokenInformation(
            token,
            informationClass,
            IntPtr.Zero,
            0,
            out var requiredLength);
        var error = Marshal.GetLastWin32Error();
        if (requiredLength == 0 || error != ErrorInsufficientBuffer)
        {
            throw new Win32Exception(error, "无法读取 Windows 身份令牌信息。");
        }

        var buffer = Marshal.AllocHGlobal(checked((int)requiredLength));
        if (GetTokenInformation(
                token,
                informationClass,
                buffer,
                requiredLength,
                out _))
        {
            return buffer;
        }

        error = Marshal.GetLastWin32Error();
        Marshal.FreeHGlobal(buffer);
        throw new Win32Exception(error, "无法读取 Windows 身份令牌信息。");
    }

    private static GenericMapping FileGenericMapping => new()
    {
        GenericRead = 0x00120089,
        GenericWrite = 0x00120116,
        GenericExecute = 0x001200A0,
        GenericAll = 0x001F01FF
    };

    private sealed record PathSecurityInspection(
        uint CurrentUserAccess,
        uint UntrustedMutationAccess,
        string? UntrustedPrincipal);

    private sealed record SecurityDescriptorInspection(
        uint UntrustedMutationAccess,
        string? UntrustedPrincipal);

    private readonly record struct ValidationOutcome(string? Message, ValidationFailureCategory Category)
    {
        public static readonly ValidationOutcome Success = new(null, ValidationFailureCategory.None);

        public bool WasSuccessful => Message is null;

        public static ValidationOutcome Failure(string message, ValidationFailureCategory category) =>
            new(message, category);
    }

    private enum ValidationFailureCategory
    {
        None,
        Other,
        UnsupportedVolumeOrMapping,
        ReparsePoint,
        ApplicationTreeAcl,
        AncestorReplacementRisk
    }

    private enum TokenInformationClass
    {
        Groups = 2,
        ElevationType = 18,
        LinkedToken = 19
    }

    private enum TokenElevationType
    {
        Default = 1,
        Full = 2,
        Limited = 3
    }

    private enum SecurityImpersonationLevel
    {
        Anonymous,
        Identification,
        Impersonation,
        Delegation
    }

    private enum SeObjectType
    {
        Unknown,
        FileObject
    }

    [Flags]
    private enum SecurityInformation : uint
    {
        Owner = 0x00000001,
        Group = 0x00000002,
        Dacl = 0x00000004
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GenericMapping
    {
        public uint GenericRead;
        public uint GenericWrite;
        public uint GenericExecute;
        public uint GenericAll;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenGroupsHeader
    {
        public uint GroupCount;
        public SidAndAttributes FirstGroup;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint DateTimeLow;
        public uint DateTimeHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public FileTime CreationTime;
        public FileTime LastAccessTime;
        public FileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        TokenInformationClass tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateToken(
        SafeAccessTokenHandle existingToken,
        SecurityImpersonationLevel impersonationLevel,
        out SafeAccessTokenHandle newToken);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EqualSid(IntPtr sid1, IntPtr sid2);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(
        string deviceName,
        StringBuilder targetPath,
        int maximumLength);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern uint GetSecurityInfo(
        SafeFileHandle handle,
        SeObjectType objectType,
        SecurityInformation securityInfo,
        out IntPtr owner,
        out IntPtr group,
        out IntPtr dacl,
        out IntPtr sacl,
        out IntPtr securityDescriptor);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern uint GetSecurityDescriptorLength(IntPtr securityDescriptor);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AccessCheck(
        IntPtr securityDescriptor,
        SafeAccessTokenHandle clientToken,
        uint desiredAccess,
        ref GenericMapping genericMapping,
        IntPtr privilegeSet,
        ref uint privilegeSetLength,
        out uint grantedAccess,
        [MarshalAs(UnmanagedType.Bool)] out bool accessStatus);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle,
        out ByHandleFileInformation fileInformation);
}
