namespace Gloaming.ActorIdentity;

/// <summary>Distinguishes explicit anonymity from human and system identities. Zero is invalid.</summary>
public enum ActorKind
{
    /// <summary>Explicitly unauthenticated execution, subject to consumer policy.</summary>
    Anonymous = 1,

    /// <summary>An authenticated application person, after consumer identity resolution.</summary>
    Human = 2,

    /// <summary>A consumer-selected system identity, independent of human admission.</summary>
    System = 3,
}
