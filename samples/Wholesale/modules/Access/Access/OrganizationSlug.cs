namespace Gloaming.Samples.Wholesale.Access;

internal static class OrganizationSlug
{
    internal static string? Canonicalize(string candidate)
    {
        if (
            candidate.Length is < 1 or > 63
            || !Alphanumeric(candidate[0])
            || !Alphanumeric(candidate[^1])
        )
            return null;
        foreach (char character in candidate)
            if (!Alphanumeric(character) && character != '-')
                return null;
        return candidate.ToLowerInvariant();
    }

    private static bool Alphanumeric(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
}
