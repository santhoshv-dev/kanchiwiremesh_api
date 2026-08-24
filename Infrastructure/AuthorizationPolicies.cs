namespace KanchimeshAPI.Infrastructure;

public static class AuthorizationPolicies
{
    public const string Administrator = "Administrator";
    public const string PasswordChange = "PasswordChange";
}

public static class JwtClaimTypes
{
    public const string MustChangePassword = "must_change_password";
    public const string UserVersion = "user_version";
}
