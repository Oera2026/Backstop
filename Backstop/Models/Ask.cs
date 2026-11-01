namespace Backstop.Models;

/// <summary>One line of the Ask Me conversation, passed back so the assistant can follow what was said.</summary>
public sealed record AskTurn(bool FromUser, string Text);

/// <summary>What Ask Me says back. <see cref="Href"/> is a page in the app, never an address from the message.</summary>
public sealed record AskReply(string Text, string? Href = null, string? LinkText = null, bool HelperTold = false, FailureInfo? Failure = null);
