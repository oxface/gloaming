namespace Gloaming.Samples.Wholesale.Access.Contracts;

public sealed record ExternalIdentity
{
    public ExternalIdentity(string issuer, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Issuer = issuer;
        Subject = subject;
    }

    public string Issuer { get; }
    public string Subject { get; }
}
