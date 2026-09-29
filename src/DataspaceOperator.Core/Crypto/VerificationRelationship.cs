namespace DataspaceOperator.Core.Crypto;

/// <summary>
/// The W3C DID verification relationships DCP distinguishes. Which one applies depends on what is
/// being verified, and the spec is specific about it:
/// <list type="bullet">
///   <item><c>Authentication</c> — the signature over a Verifiable Presentation
///     (DCP "Presentation Validation", step 3).</item>
///   <item><c>AssertionMethod</c> — the issuer's signature over a Verifiable Credential.</item>
///   <item><c>CapabilityInvocation</c> — a Self-Issued ID Token
///     (DCP "Validating Self-Issued ID Tokens", step 3).</item>
/// </list>
/// </summary>
public enum VerificationRelationship
{
    /// <summary>No relationship check — any key listed in <c>verificationMethod</c> is accepted.</summary>
    Any,
    Authentication,
    AssertionMethod,
    CapabilityInvocation,
}
