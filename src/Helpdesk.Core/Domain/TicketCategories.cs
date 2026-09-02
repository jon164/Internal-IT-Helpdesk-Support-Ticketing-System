namespace Helpdesk.Core.Domain;

/// <summary>
/// The categories a request may be filed under.
/// </summary>
/// <remarks>
/// <para>
/// A fixed catalogue rather than free text. The problem definition wants demand grouped so the
/// recurring causes become visible; free text would give "printer", "Printer", "printing issue" and
/// "the printer again" as four separate categories, and the reporting that justifies the whole system
/// would be worth nothing.
/// </para>
/// <para>
/// It lives here, in the domain, rather than being derived from the tickets already in the database.
/// Deriving it seemed reasonable until the application was made to start empty: the first requester
/// would then have been offered no categories at all, and the second would have been offered only
/// whatever the first happened to choose. A catalogue that depends on its own contents cannot be
/// populated.
/// </para>
/// </remarks>
public static class TicketCategories
{
    /// <summary>Ordered for the form, not alphabetically: the common cases first.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "Account & Access",
        "Hardware",
        "Software",
        "Network",
        "Printing",
        "AV & Meeting Rooms",
        "Point of Sale",
        "Mobile & Telephony",
        "Email"
    ];

    public static bool IsKnown(string? category) =>
        category is not null && All.Contains(category.Trim(), StringComparer.OrdinalIgnoreCase);
}
