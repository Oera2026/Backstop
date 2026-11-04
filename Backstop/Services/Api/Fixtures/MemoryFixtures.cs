using Backstop.Models;

namespace Backstop.Services.Api.Fixtures;

/// <summary>Memory cards from earlier Show me and Explain sessions. MOCK: synthetic, dates relative to today.</summary>
public static class MemoryFixtures
{
    public static IReadOnlyList<string> SuggestedQuestions { get; } =
    [
        "How did I reset the internet?",
        "How do I wash my jumpers?",
        "What did the council letter say?",
    ];

    public static List<MemoryCard> Seed()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return
        [
            new("m1", "Resetting the internet box", today.AddDays(-12),
                "The internet stopped working. Turning the box off and on fixed it.",
                "router", ["internet", "home", "wifi"],
                [
                    "Find the white box with the lights, next to the phone socket.",
                    "Turn it off at the wall switch.",
                    "Wait for one minute.",
                    "Turn it back on.",
                    "Wait until the light on the front turns green.",
                ],
                "Last time you reset the internet box by turning it off at the wall for one minute, then back on.",
                MemorySource.ShowMe, Shared: true),

            new("m2", "Washing woollen jumpers", today.AddDays(-20),
                "Jumpers go on the Wool setting so they do not shrink.",
                "washer", ["washing", "clothes", "jumper"],
                [
                    "Check the label says you can machine wash it.",
                    "Turn the big dial to \"Wool\".",
                    "Use half a cap of washing liquid.",
                    "Press Start.",
                    "Lay the jumper flat to dry.",
                ],
                "Last time you washed your jumpers by turning the big dial to \"Wool\" and laying them flat to dry.",
                MemorySource.ShowMe, Shared: false),

            new("m3", "Council Tax letter", today.AddDays(-30),
                "The council checked you still live alone. You sent the form back in the free envelope.",
                "letter", ["letters", "council", "money"],
                [
                    "Find the form in the envelope.",
                    "Tick if you still live alone.",
                    "Sign and date the form.",
                    "Post it back in the free envelope.",
                ],
                "Last time, the council letter asked if you still live alone. You filled in the form and posted it back in the free envelope.",
                MemorySource.Explain, Shared: true),

            new("m4", "Making the bed", today.AddDays(-41),
                "The fitted sheet goes on one corner at a time.",
                "bed", ["home", "bedroom", "sheets"],
                [
                    "Put the fitted sheet over one top corner.",
                    "Pull it over the other three corners.",
                    "Smooth it flat.",
                    "Shake the duvet and lay it on top.",
                ],
                "Last time you made the bed by putting the fitted sheet over one corner at a time.",
                MemorySource.ShowMe, Shared: false),

            new("m5", "Bus pass renewal form", today.AddDays(-58),
                "You filled in the bus pass form and added a new photo.",
                "form", ["forms", "bus", "travel"],
                [
                    "Write your name in capital letters.",
                    "Stick the new photo in the box at the top.",
                    "Sign inside the box.",
                    "Post it to the address on the back.",
                ],
                "Last time you renewed your bus pass by filling in the form in capital letters and sticking a new photo in the top box.",
                MemorySource.ShowMe, Shared: false),
        ];
    }

    /// <summary>Extra words people use for the same thing, so "wifi" finds the internet box.</summary>
    public static IReadOnlyDictionary<string, string[]> Synonyms { get; } = new Dictionary<string, string[]>
    {
        ["internet"] = ["wifi", "router", "broadband", "box", "online"],
        ["washing"] = ["wash", "laundry", "machine", "jumpers", "jumper"],
        ["council"] = ["letter", "tax"],
        ["bed"] = ["sheet", "sheets", "duvet"],
        ["form"] = ["forms", "bus", "pass"],
    };
}
