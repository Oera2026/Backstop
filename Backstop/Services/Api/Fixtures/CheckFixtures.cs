using Backstop.Models;

namespace Backstop.Services.Api.Fixtures;

/// <summary>
/// ALL DATA HERE IS SYNTHETIC. Made-up people, numbers from the drama ranges (07700 900xxx),
/// and no real brands or domains. MOCK: a real back end replaces these with the user's own data.
/// </summary>
public static class HouseholdFixture
{
    public static Household Household { get; } = new("Margaret", "Priya", "Daughter");
}

public static class CheckFixtures
{
    /// <summary>"Hi Mum, new number" asking for gift cards. Expected: Don't reply.</summary>
    public const string GiftCardScam =
        "Hi Mum, it's me. I dropped my phone in the bath and this is my new number.\n\n" +
        "I need to pay a bill today or I'll get a fine. Can you buy £200 of gift cards from the shop and send me the codes on the back?\n\n" +
        "Please keep this between us, I don't want Dad to worry. Thanks Mum x";

    /// <summary>A friendly family message. Expected: Looks safe.</summary>
    public const string SafeMessage =
        "Hi Mum, just to say we will come round on Sunday at one o'clock. Tomas wants to show you his drawing of the garden. Love, Priya";

    /// <summary>A delivery note that rushes you. Expected: Be careful.</summary>
    public const string ParcelNudge =
        "Your parcel could not be delivered. Please book a new delivery time today: https://parcel-times.example.net/book";

    /// <summary>
    /// A "friend" who builds trust over weeks, then asks for secrecy, another app and money.
    /// Expected: risk rises message by message, ending in Don't reply.
    /// </summary>
    public const string FriendThread =
        "Daniel: Hello Margaret, I found you through the gardening group. Your roses are beautiful!\n" +
        "Margaret: Thank you, that is kind of you.\n" +
        "Daniel: I am working on a ship far away. Talking to you is the best part of my day.\n" +
        "Daniel: I feel like I have known you for years. You are very special to me, my darling.\n" +
        "Margaret: That is a lovely thing to say.\n" +
        "Daniel: Please don't tell your family about us yet. They would not understand.\n" +
        "Daniel: Can we chat on the other app instead? It is more private. My number is 07700 900456.\n" +
        "Daniel: My bank card has stopped working out here. I need £300 for my flight home.\n" +
        "Daniel: Could you buy gift cards and send me the codes today? I will pay you back. Please hurry.";

    /// <summary>What the mock "reads" from any picture the user adds to Check.</summary>
    public const string PictureText = GiftCardScam;

    /// <summary>Examples the demo panel can open in Check (?example=...).</summary>
    public static IReadOnlyDictionary<string, (CheckKind Kind, string Text)> Examples { get; } =
        new Dictionary<string, (CheckKind, string)>
        {
            ["giftcard"] = (CheckKind.Message, GiftCardScam),
            ["safe"] = (CheckKind.Message, SafeMessage),
            ["parcel"] = (CheckKind.Message, ParcelNudge),
            ["friend"] = (CheckKind.Conversation, FriendThread),
        };
}
