using System.Globalization;
using Backstop.Models;

namespace Backstop.Services.Api.Fixtures;

/// <summary>
/// The council letter Explain is built around, written at three reading levels.
/// MOCK: a real back end rewrites any text. The council, reference and phone number are made up
/// (01632 960xxx is a drama-only range).
/// </summary>
public static class ExplainFixtures
{
    private static DateOnly Today => UserClock.Server.Today;
    private static DateOnly Due => Today.AddDays(14);
    private static DateOnly Increase => new DateOnly(Due.Year, Due.Month, 1).AddMonths(1);
    private static string Full(DateOnly d) => d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
    private static string Short(DateOnly d) => d.ToString("d MMMM", CultureInfo.InvariantCulture);

    /// <summary>The letter, with dates counted from today so it never goes out of date.</summary>
    public static string CouncilLetter =>
        "Oakfield Borough Council\nRevenues Team\n\n" +
        "Council Tax: Single Person Discount review\nReference: 4410 2297\n\n" +
        $"Dear {HouseholdFixture.Household.UserName},\n\n" +
        "We are reviewing your entitlement to the Single Person Discount on your Council Tax account. " +
        "This discount reduces your bill by 25% because you are the only adult living at your home.\n\n" +
        $"Please confirm whether you still live alone by completing the enclosed declaration form and returning it to us by {Full(Due)}. " +
        "You can use the prepaid envelope provided.\n\n" +
        $"If we do not receive your reply by this date, the discount may be removed and your bill will increase from {Full(Increase)}.\n\n" +
        "If you need help, please call us on 01632 960 123, Monday to Friday, 9am to 5pm.\n\n" +
        "Yours sincerely,\nRevenues Team";

    /// <summary>Whether this text looks like the council letter.</summary>
    public static bool IsCouncilLetter(string text) =>
        text.Contains("Single Person Discount", StringComparison.OrdinalIgnoreCase)
        && text.Contains("Oakfield Borough Council", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The hand-written explanation is only used when nothing in the text looks wrong. A look-alike that asks for
    /// money or secrets, or carries a link, is explained like any other text, with its warnings.
    /// </summary>
    public static bool UsesFixture(string text) => IsCouncilLetter(text) && MockRiskRules.Scan(text).Score == 0;

    private static string T => HouseholdFixture.Household.TrustedPersonName;

    public const string Title = "Council Tax letter";
    public static string Deadline => $"Reply by {Full(Due)}";

    // Each piece of wording at Very simple, Simple and Standard.
    private sealed record L(string VerySimple, string Simple, string Standard)
    {
        public string At(ReadingLevel level) => level switch
        {
            ReadingLevel.VerySimple => VerySimple,
            ReadingLevel.Simple => Simple,
            _ => Standard,
        };
    }

    private static L Overview => new(
        $"This letter is from the council. They want to know if you still live on your own. Fill in the form and post it back by {Short(Due)}.",
        $"This letter is from Oakfield Council. You pay less Council Tax because you live on your own. They want you to say if that is still true. Fill in the form and post it back by {Full(Due)}.",
        $"Oakfield Borough Council is checking your Single Person Discount, which takes 25% off your Council Tax because you live alone. Confirm your situation on the enclosed form and return it by {Full(Due)}, or the discount may stop from {Short(Increase)}.");

    private static (string Heading, L Body)[] Summary =>
    [
        ("Who it is from", new(
            "The council. They look after bins and roads.",
            "Oakfield Borough Council. This is the part of the council that deals with money.",
            "The Revenues Team at Oakfield Borough Council, who manage Council Tax bills.")),
        ("Why they wrote", new(
            "You pay less because you live alone. They are checking this is still true.",
            "You get money off your Council Tax because you live alone. They check this every so often.",
            "They are reviewing whether you still qualify for the 25% Single Person Discount on your account.")),
        ("What you need to do", new(
            "Fill in the form. Post it back. You do not need a stamp.",
            "Fill in the form that came with the letter. Post it back in the envelope. You do not need a stamp.",
            "Complete the enclosed declaration form and return it in the prepaid envelope.")),
        ("If you do nothing", new(
            "Your bill may go up in December.",
            $"If they do not hear from you by {Short(Due)}, your bill may go up from {Short(Increase)}.",
            $"Without a reply by {Full(Due)}, the discount may be removed and your bill will increase from {Full(Increase)}.")),
    ];

    private static (string Heading, L Body)[] Steps =>
    [
        ("Step 1: Find the form", new(
            "Look in the envelope. Find the form.",
            "Look in the envelope for a form called a declaration form.",
            "Find the declaration form enclosed with this letter.")),
        ("Step 2: Fill it in", new(
            "Tick yes if you live alone. Sign it.",
            "Tick \"Yes\" if you still live alone. Sign and date it at the bottom.",
            "Confirm whether you still live alone, then sign and date the declaration.")),
        ("Step 3: Post it back", new(
            "Put it in the envelope. Post it. No stamp needed.",
            "Put the form in the envelope that came with it. You do not need a stamp.",
            "Return the form in the prepaid envelope provided.")),
        ("Step 4: Do it in time", new(
            $"Post it before {Short(Due)}.",
            $"Post it so it gets there by {Full(Due)}.",
            $"Make sure it arrives by {Full(Due)} to keep the discount.")),
    ];

    private static (string Heading, L Body)[] Questions =>
    [
        ("What is this letter?", new(
            "A letter from the council about money off your bill.",
            "A letter from the council checking you still get money off your Council Tax.",
            "A Single Person Discount review from Oakfield Borough Council.")),
        ("Do I have to do anything?", new(
            "Yes. Fill in the form and post it.",
            "Yes. Fill in the form and post it back.",
            "Yes. Complete and return the enclosed declaration form.")),
        ("When by?", new(
            $"By {Short(Due)}.",
            $"By {Full(Due)}.",
            $"It must arrive by {Full(Due)}.")),
        ("What if I miss it?", new(
            "Your bill may go up.",
            $"You may lose the discount and pay more from {Short(Increase)}.",
            $"The discount may be removed and your bill will increase from {Full(Increase)}.")),
        ("Who can help?", new(
            $"Ring the council. Or ask {T}.",
            $"You can ring the council on 01632 960 123, Monday to Friday, or ask {T}.",
            $"Call 01632 960 123, Monday to Friday, 9am to 5pm, or ask {T} to help.")),
    ];

    public static IReadOnlyList<string> Actions =>
    [
        "Find the form in the envelope",
        "Tick if you still live alone",
        "Sign and date the form",
        $"Post it back by {Short(Due)}",
    ];

    public static string OverviewAt(ReadingLevel level) => Overview.At(level);

    public static IReadOnlyList<ExplainChunk> ChunksFor(ReadingLevel level, ExplainFormat format)
    {
        var source = format switch
        {
            ExplainFormat.Steps => Steps,
            ExplainFormat.Questions => Questions,
            _ => Summary,
        };
        return source.Select(c => new ExplainChunk(c.Heading, c.Body.At(level))).ToList();
    }

    /// <summary>Answers for "Ask a question about this", matched by words in the question.</summary>
    public static IReadOnlyList<(string[] Words, string Answer)> Answers =>
    [
        (["when", "deadline", "date", "by"], $"Post the form so it gets there by {Full(Due)}."),
        (["stamp", "envelope", "post"], "You do not need a stamp. Use the envelope that came with the letter."),
        (["pay", "money", "cost", "bill", "more"], $"You do not need to pay anything now. If you miss the date, your bill may go up from {Short(Increase)}."),
        (["scam", "real", "safe", "trust"], "This looks like a normal council letter. It does not ask for bank details. If you are unsure, ring the number on your last Council Tax bill."),
        (["live", "alone", "lodger", "someone"], "Tick yes only if no other adult lives with you. If someone has moved in, tick no."),
        (["help", "call", "phone", "ring"], $"You can ring the council on 01632 960 123, Monday to Friday, 9am to 5pm. Or ask {T} to help you."),
    ];
}

/// <summary>Hard words and what they mean, in one line each. MOCK: a real back end finds these itself.</summary>
public static class Glossary
{
    // Only words that are hard in any text. Everyday words with a hard meaning ("fine", "account") are left out.
    public static IReadOnlyDictionary<string, string> Words { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["entitlement"] = "What you have a right to get.",
        ["Single Person Discount"] = "Money off your Council Tax because you live alone.",
        ["declaration"] = "A form where you say something is true.",
        ["enclosed"] = "Put in the same envelope as this letter.",
        ["prepaid"] = "Already paid for, so you do not need a stamp.",
        ["Council Tax"] = "Money you pay the council for things like bins and street lights.",
        ["Revenues"] = "The part of the council that deals with money.",
        ["reviewing"] = "Checking again.",
        ["Reference"] = "A number that helps them find your account.",
        ["confirm"] = "Say that something is true.",
        ["sincerely"] = "A polite way to end a letter.",
        ["gift cards"] = "Cards with money on them for a shop. Scammers often ask for the codes.",
        ["verify"] = "Check that something is true.",
    };
}
