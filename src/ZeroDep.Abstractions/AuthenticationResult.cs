namespace ZeroDep.Abstractions;

/// <summary>Result of authenticating against an encrypted document.</summary>
public enum AuthenticationResult
{
    /// <summary>The document is not encrypted; no authentication was required.</summary>
    NotRequired = 0,

    /// <summary>Authenticated with the user password (or the empty/default user password).</summary>
    UserPassword,

    /// <summary>Authenticated with the owner password.</summary>
    OwnerPassword,

    /// <summary>No supplied or default password authenticated the document.</summary>
    Failed,
}
