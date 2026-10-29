using Backstop.Models;

namespace Backstop.Services.Api.Fixtures;

/// <summary>
/// The three Show me tasks the mock can "generate". MOCK: a real back end reads the photo and the goal,
/// and draws the guide picture. Here each task is matched by its words, and the picture is a drawing (see GuidePicture).
/// </summary>
public static class GuideFixtures
{
    public sealed record GuideTask(
        string Key,
        string[] Words,
        string Title,
        // Finishes "Last time you … by", for memory answers.
        string PastTense,
        string Picture,
        string[] Steps,
        Region Highlight,
        CareNote? NeedsCare,
        string[] Tags);

    public static IReadOnlyList<GuideTask> Tasks { get; } =
    [
        new("form", ["form", "fill", "sign", "declaration", "paper"],
            "Filling in a form", "filled in the council form", "form",
            [
                "Find a pen with black ink.",
                "Write your full name in the first box, in capital letters.",
                "Write your address in the next box.",
                "Tick \"Yes, I still live alone\" if that is true.",
                "Sign your name in the box at the bottom.",
                "Write today's date next to your name.",
                "Put the form in the envelope that came with it.",
            ],
            new(18, 68, 64, 18, "The box to sign in"), null, ["letters", "forms"]),

        new("bed", ["bed", "sheet", "duvet", "pillow", "bedding"],
            "Making the bed", "made the bed", "bed",
            [
                "Take off the old sheet and pillow cases.",
                "Put the fitted sheet over one top corner of the mattress.",
                "Pull it over the other three corners, one at a time.",
                "Smooth the sheet flat with your hands.",
                "Put a clean case on each pillow.",
                "Shake the duvet and lay it flat on top.",
            ],
            new(10, 52, 80, 30, "The corners of the mattress"), null, ["home", "bedroom"]),

        new("washer", ["wash", "washing", "machine", "dial", "laundry", "clothes", "jumper"],
            "Setting the washing machine", "set the washing machine", "washer",
            [
                "Check the label in your clothes says you can machine wash them.",
                "Put the clothes in and close the door until it clicks.",
                "Put one cap of washing liquid in the drawer at the top.",
                "Turn the big dial until it points to \"40 Cottons\".",
                "Press the Start button.",
                "Wait for the light to go off before you open the door.",
            ],
            new(55, 12, 30, 26, "The big dial"),
            new("Chemicals", "Washing liquid and pods can hurt if swallowed or splashed in eyes."),
            ["home", "washing"]),
    ];

    /// <summary>Goals the empty state suggests when nothing matched.</summary>
    public static IReadOnlyList<string> Suggestions { get; } = ["Make the bed", "Fill in a form", "Set the washing machine"];

    public static GuideTask? Match(string goal)
    {
        var words = goal.ToLowerInvariant();
        return Tasks
            .Select(t => (Task: t, Score: t.Words.Count(w => words.Contains(w))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Task)
            .FirstOrDefault();
    }
}
