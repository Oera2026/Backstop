using System.Text.RegularExpressions;

namespace Backstop.Services;

/// <summary>
/// One home for the "private details" wording, so incoming rules and the outgoing check cannot drift apart.
/// "pin" alone is an everyday word (a safety pin), so it only counts beside a number or as "my/your/card/bank PIN".
/// </summary>
public static class SensitivePatterns
{
    private const string BankTerms = @"\bbank details\b|\bsort code\b|\baccount (?:number|no)\b|\biban\b";
    private const string SecretTerms = @"\b(?:password|passcode|security code|one[- ]time code|cvv|cvc)\b|\b(?:my|your|card|bank) pin\b|\bpin (?:number|code)\b|\bpin\W{0,4}(?:is\W{0,4})?\d{4,6}\b";

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    /// <summary>Bank details, sort codes and account numbers.</summary>
    public static Regex Bank { get; } = new(BankTerms, Options);

    /// <summary>Passwords, PINs and codes that give access.</summary>
    public static Regex Secret { get; } = new(SecretTerms, Options);

    /// <summary>Anything a message may not ask for or give away: card, bank and secret-code wording.</summary>
    public static Regex Private { get; } = new($@"\bcard (?:details|number)\b|\b(?:log[- ]?in|sign[- ]?in|online banking) details\b|\bdate of birth\b|\bmemorable word\b|\bmother'?s maiden name\b|\bnational insurance number\b|\bpassport (?:number|details)\b|{BankTerms}|{SecretTerms}", Options);
}
