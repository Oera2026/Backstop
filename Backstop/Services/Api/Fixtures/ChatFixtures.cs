using Backstop.Models;

namespace Backstop.Services.Api.Fixtures;

/// <summary>MOCK: made-up people and made-up replies. Nothing here is real.</summary>
public static class ChatFixtures
{
    /// <summary>The helper who is also the household's trusted person. Her side is the real /helper view.</summary>
    public const string TrustedId = "p-priya";

    public static IReadOnlyList<ChatPerson> People { get; } =
    [
        new(TrustedId, "Priya", PersonRole.Helper, "Daughter, your helper"),
        new("p-sam", "Sam", PersonRole.Helper, "Care worker, your helper"),
        new("p-arthur", "Arthur", PersonRole.User, "Friend from the lunch club"),
        new("p-joan", "Joan", PersonRole.User, "Neighbour"),
    ];

    public static ChatPerson? Find(string id) => People.FirstOrDefault(p => p.Id == id);

    /// <summary>What a made-up person says back. <paramref name="n"/> counts the user's messages to them.</summary>
    public static string Reply(string personId, int n) => personId switch
    {
        "p-sam" => "Thanks Margaret. I got your message. I will answer you soon.",
        "p-arthur" when n <= 1 => "Hello Margaret! Are you coming to lunch club on Thursday?",
        // Arthur's account has been taken over: the second message is a scam, so the checks hold it.
        "p-arthur" => "Lovely. I need a quick favour. Please buy two gift cards today and send me the codes. Keep it secret, it is a surprise.",
        "p-joan" => "Hello Margaret. It is a lovely day. I hope you are well.",
        _ => "",
    };
}
