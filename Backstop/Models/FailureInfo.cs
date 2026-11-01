namespace Backstop.Models;

/// <summary>
/// Why something could not finish. <see cref="Message"/> is plain words for anyone, with the next step.
/// <see cref="Technical"/> is for someone investigating. It never holds message text, keys or personal details.
/// </summary>
public sealed record FailureInfo(string Message, string Technical);
