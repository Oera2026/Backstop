using Backstop.Models;

namespace Backstop.Services.Api.Fixtures;

/// <summary>
/// Stories wording. MOCK: a real back end writes these with a language model, in the
/// user's own situation. Here each situation is matched by its words. ALL DATA IS SYNTHETIC.
/// Stories follow the usual social-story shape: what will happen, what people do, how it may feel, what I can do.
/// </summary>
public static class StoryFixtures
{
    public sealed record StoryTemplate(string Key, string[] Words, string Title, StoryPage[] Pages);

    /// <summary>Topics a trusted person should see first. The story is held until they decide.</summary>
    public static readonly string[] SensitiveWords =
        ["money", "bank", "loan", "debt", "stranger", "argu", "shout", "angry", "hurt", "tablet", "medicine", "police", "court"];

    public static IReadOnlyList<StoryTemplate> Templates { get; } =
    [
        new("appointment", ["dentist", "doctor", "gp", "appointment", "nurse", "hospital", "clinic"],
            "Going to my appointment",
            [
                new("What is happening", "I have an appointment. A person will look after my health. I will go there and come home again.", "door"),
                new("Getting there", "I will leave home in good time. I will take my keys and my phone. It is fine to arrive a little early.", "bus"),
                new("When I arrive", "There is a desk with a person behind it. I can say my name. They will tell me where to sit.", "desk"),
                new("Waiting", "Sometimes I wait for a while. Waiting is normal. I can look at my phone or breathe slowly.", "chair"),
                new("Meeting the person", "The person will say hello. They will ask me some questions. I can say, \"Please can you say that again?\"", "talk"),
                new("How I might feel", "I might feel nervous. Many people feel nervous. That is okay. It does not mean anything is wrong.", "feel"),
                new("If it feels like too much", "I can say, \"I need a minute.\" I can ask for a break. I can ask for {helper} to be told.", "calm"),
                new("Afterwards", "When it is over I can go home. I can have a drink and a rest. I did something hard.", "home"),
            ]),
        new("travel", ["bus", "train", "travel", "journey", "taxi", "station", "airport"],
            "Going on a journey",
            [
                new("What is happening", "I am going on a journey. I will get on a bus or a train. I will get off at the right place.", "bus"),
                new("Before I go", "I will check the time. I will put my ticket and my phone in my bag. I will leave a few minutes early.", "door"),
                new("At the stop", "Other people will be waiting too. It can be busy and noisy. I can stand a little to one side.", "chair"),
                new("If it is late", "Sometimes a bus or train is late. That is normal. I can wait, or I can ask someone who works there.", "talk"),
                new("How I might feel", "I might feel worried or rushed. That is okay. I can take a slow breath.", "feel"),
                new("If it feels like too much", "I can get off at the next stop. I can sit down and call {helper}.", "calm"),
                new("Arriving", "When I get there I will look for the way out. I did it.", "home"),
            ]),
        new("visit", ["party", "friend", "visit", "meeting", "meet", "birthday", "family", "wedding", "dinner", "lunch"],
            "Seeing people",
            [
                new("What is happening", "I am going to see some people. They are people I know, or I may meet some new people.", "door"),
                new("When I arrive", "People will say hello. I can say hello back. I do not have to say a lot.", "talk"),
                new("Talking", "People take turns to talk. I can listen. When I want to speak, I can wait for a gap. Short answers are fine.", "talk"),
                new("It can be noisy", "There might be lots of voices and music. That can be tiring. I can go to a quieter place for a bit.", "chair"),
                new("How I might feel", "I might feel excited, shy or tired. All of these are okay.", "feel"),
                new("If it feels like too much", "I can say, \"I am going to get some air.\" I can leave early. That is allowed.", "calm"),
                new("Afterwards", "I can go home and rest. I went, and that was brave.", "home"),
            ]),
        new("shop", ["shop", "shopping", "supermarket", "market", "buy", "queue", "post office"],
            "Going to the shops",
            [
                new("What is happening", "I am going to the shop to get what I need. I will pay and then I will go home.", "shop"),
                new("Before I go", "I will make a short list. I will take my bag and my purse.", "door"),
                new("In the shop", "There may be lots of people. I can take my time. I can ask a worker where something is.", "talk"),
                new("The queue", "I will wait in a line. When it is my turn the person will say hello. They will tell me how much to pay.", "desk"),
                new("How I might feel", "A busy shop can feel loud and fast. That is okay. I can take a slow breath.", "feel"),
                new("If it feels like too much", "I can leave my basket and go outside. I can call {helper}.", "calm"),
                new("Afterwards", "I can go home and put things away. Done.", "home"),
            ]),
        new("change", ["change", "new", "moving", "move", "different", "start", "first day", "job", "course", "school"],
            "Something new is happening",
            [
                new("What is happening", "Something is going to be different. I know about it before it happens.", "door"),
                new("What will stay the same", "Some things will stay the same. My home, my family and my favourite things are still here.", "home"),
                new("What will be new", "There will be new places, new people or a new plan. I will not have to learn everything at once.", "talk"),
                new("Asking questions", "It is okay to ask questions. I can write them down first. I can ask {helper} to come with me.", "desk"),
                new("How I might feel", "I might feel worried, excited or a bit lost. Feelings can change from hour to hour.", "feel"),
                new("If it feels like too much", "I can stop and sit down. I can ask for more time. I can tell someone I trust.", "calm"),
                new("Later", "After some time, new things start to feel normal. I can be proud of trying.", "home"),
            ]),
    ];

    public static StoryTemplate Generic { get; } = new("other", [], "Something is coming up",
    [
        new("What is happening", "Something is coming up. I know about it before it happens.", "door"),
        new("What will happen", "I will go there. I will do what I need to do. Then I will come home.", "bus"),
        new("People", "There may be other people. They might say hello. I can say hello back.", "talk"),
        new("How I might feel", "I might feel nervous or unsure. That is okay.", "feel"),
        new("If it feels like too much", "I can stop and take a slow breath. I can ask for a break. I can tell {helper}.", "calm"),
        new("Afterwards", "When it is over I can rest. I did it.", "home"),
    ]);

    public static bool IsSensitive(string text) =>
        SensitiveWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    public static StoryTemplate Match(string text) =>
        Templates.FirstOrDefault(t => t.Words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase))) ?? Generic;

}
