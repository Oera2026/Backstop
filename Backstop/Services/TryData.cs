using Backstop.Models;

namespace Backstop.Services;

/// <summary>Made-up examples for the Try it page. All names and domains are fictional (example.* and .test only).</summary>
public static class TryData
{
    public static IReadOnlyList<TrySample> Samples { get; } =
    [
        new("phishing", "Phishing link",
            new("Northbank Security", "security@northbank-alerts.example.net", "Your account is locked",
                "We saw a sign in we did not expect. Verify your password within 24 hours or your account will be closed.\n\nhttps://northbank-verify.example.net/login")),

        new("redirect", "Redirect link",
            new("Parcel Desk", "notify@parcel-update.example.net", "Parcel fee to pay",
                "Your parcel is waiting. Pay a £1.45 re-delivery fee here: http://192.0.2.45/track?id=88\n\nYour parcel will be returned in 24 hours.")),

        new("install", "Install software",
            new("Windows Support Team", "support@secure-helpdesk.example.net", "Your computer has a virus",
                "Your computer has a virus. Our engineer must fix it now.\n\nPlease install AnyDesk so we can connect to your computer and read us the 9 digit code.")),

        new("off-thread", "Move to WhatsApp",
            new("Graham Pike", "graham.pike@example.org", "Re: Allotment club",
                "Lovely to hear from you again!\n\nCould you message me on WhatsApp instead? My number is 07700 900123. It is easier for chatting.\n\nGraham")),

        new("family", "Normal family email",
            new("Priya Sandhu", "priya.sandhu@example.com", "Photos from the beach",
                "Hi Mum,\n\nHere are the photos from Saturday. Tomas loved the sea!\n\nCan I ring you on Sunday?\n\nLove, Priya")),

        new("legit", "Tricky but real",
            new("Oakfield Pharmacy", "hello@oakfield-pharmacy.example.org", "Your prescription is ready today",
                "Dear Mr Bell,\n\nYour prescription is ready to collect today. Please bring your appointment letter.\n\nOpening times: https://oakfield-pharmacy.example.org/opening-times\n\nOakfield Pharmacy")),
    ];
}
