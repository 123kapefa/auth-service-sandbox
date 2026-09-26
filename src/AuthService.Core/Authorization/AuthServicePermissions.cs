namespace AuthService.Core.Authorization;

/// <summary>Права сервиса. В коде — только эти константы, строки прав не пишутся руками.</summary>
public static class AuthServicePermissions
{
    public static class Profiles
    {
        public const string VIEW = "profile.view";
    }

    public static class Users
    {
        public const string VIEW = "users.view";
        public const string BLOCK = "users.block";
        public const string MANAGE_ROLES = "users.roles.manage";
    }

    public static class Audit
    {
        public const string VIEW = "audit.view";
    }
}
