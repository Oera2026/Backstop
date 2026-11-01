using Backstop.Models;

namespace Backstop.Services.Api;

public sealed partial class MockBackstopApi
{
    private const int MaxAskLength = 1000;

    /// <summary>
    /// Ask Me. The AI writes the answer. The local rules run first and can only add care: a sign that someone may
    /// be in danger always tells the helper, whatever the AI says, and if the AI cannot answer the person is told so
    /// plainly. Nothing here is a canned answer standing in for the AI.
    /// </summary>
    public async Task<AskReply> AskAsync(IReadOnlyList<AskTurn> history, string message, CancellationToken ct = default)
    {
        await Wait(200, ct);
        var text = (message ?? "").Trim();
        if (text.Length == 0) throw new BackstopApiException("Please type something first.");
        if (text.Length > MaxAskLength) throw new BackstopApiException("That is a bit long for me. Please make it shorter.");

        var floor = MockRiskRules.Scan(Defang.ForUser(text));
        var localRisk = floor.Flags.Any(f => f.Id == "at-risk");
        var looksScammy = floor.Flags.Any(f => f.Id is not ("at-risk" or "injection"));

        AssistantReply reply;
        if (assistant is null) reply = new("", false, FailureNote: "No assistant configured",
            Failure: new("Ask Me is not switched on yet. Please tell the person who looks after this app.", "Code: NO_ASSISTANT\nWhat: No IAssistant is registered."));
        else
        {
            try { reply = await assistant.ReplyAsync([.. history.Select(t => new AssistantTurn(t.FromUser, t.Text))], text, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { reply = new("", false, FailureNote: "Assistant failed",
                Failure: new("Something went wrong. Please try again in a minute.", $"Code: ASSISTANT_EXCEPTION\nWhat: {ex.GetType().Name}\nTime: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")); }
        }

        // Someone may be in danger, or the assistant wants a human: the helper is always told.
        var told = localRisk || reply.NeedsHuman || reply.AtRisk;
        if (told) Escalate(text, atRisk: localRisk || reply.AtRisk);

        string answer;
        if (reply.Success)
        {
            answer = reply.Text;
            if (told && !answer.Contains("helper", StringComparison.OrdinalIgnoreCase) && !answer.Contains(Helper, StringComparison.OrdinalIgnoreCase))
                answer += $" I have told {Helper}.";
        }
        else
        {
            // Say what went wrong, in plain words. The technical detail travels separately.
            var plain = reply.Failure?.Message ?? "Sorry, I could not think of an answer just now. Please try again in a moment.";
            answer = told ? $"{plain} I have asked {Helper} to look at your message." : plain;
        }

        // Only fixed pages of the app can be suggested, never anything taken from the message or the reply.
        var suggest = looksScammy ? "check" : reply.Suggest;
        var (href, link) = suggest switch
        {
            "check" => ("check", "Check a message"),
            "explain" => ("explain", "Explain something"),
            "show-me" => ("show-me", "Show me how"),
            "story" => ("stories/new", "Make a story"),
            _ => ((string?)null, (string?)null),
        };
        return new AskReply(answer, href, link, told, reply.Success ? null : reply.Failure);
    }

    /// <summary>Puts the conversation in the helper's queue, held, so a person looks at it.</summary>
    private void Escalate(string text, bool atRisk)
    {
        var flag = atRisk
            ? MockRiskRules.AllFlags.First(f => f.Id == "at-risk")
            : new RiskFlag("review-needed", "hourglass", "Needs helper check", "Your helper will take a look at this.");
        var result = new CheckResult(NewId("chk"), CheckKind.Message, Verdict.BeCareful,
            atRisk ? "Someone may need help." : "Ask Me was not sure how to help.",
            [flag], Defang.ForUser(text), [], null, ReviewStatus.Held, false, DateTimeOffset.Now);
        lock (_gate) _checks[result.Id] = new(result, text, DateTimeOffset.Now);
        Changed?.Invoke();
    }
}
