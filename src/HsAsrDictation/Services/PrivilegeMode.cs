namespace HsAsrDictation.Services;

public enum PrivilegeMode
{
    Standard,
    Administrator
}

public static class PrivilegeModeExtensions
{
    public static string ToDisplayText(this PrivilegeMode mode) =>
        mode == PrivilegeMode.Administrator ? "管理员模式" : "普通模式";

    public static string ToShortDisplayText(this PrivilegeMode mode) =>
        mode == PrivilegeMode.Administrator ? "管理员" : "普通";

    public static string ToKey(this PrivilegeMode mode) =>
        mode == PrivilegeMode.Administrator ? "admin" : "standard";
}
