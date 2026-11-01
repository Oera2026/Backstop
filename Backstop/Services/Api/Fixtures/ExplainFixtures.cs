using System.Globalization;
using Backstop.Models;

namespace Backstop.Services.Api.Fixtures;

/// <summary>
/// A sample letter the demo can paste into Explain. The AI does the explaining.
/// The council, reference and phone number are made up
/// (01632 960xxx is a drama-only range).
/// </summary>
public static class ExplainFixtures
{
    private static DateOnly Today => UserClock.Server.Today;
    private static DateOnly Due => Today.AddDays(14);
    private static DateOnly Increase => new DateOnly(Due.Year, Due.Month, 1).AddMonths(1);
    private static string Full(DateOnly d) => d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

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
}
