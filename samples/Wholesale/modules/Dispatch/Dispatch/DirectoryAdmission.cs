namespace ModulithFoundry.Samples.Wholesale.Dispatch;

internal static class DirectoryAdmission
{
    internal static void Require(string organization)
    {
        if (organization is not ("wholesale-alpha" or "wholesale-beta"))
            throw new InvalidDataException("Dispatch has not admitted this Organization.");
    }
}
