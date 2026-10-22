namespace Gloaming.Messaging.RabbitMQ;

/// <summary>Explicit native phase timeouts; neither is a process or publisher deadline.</summary>
/// <param name="ProtocolTimeout">Native protocol continuation timeout, including close RPCs.</param>
/// <param name="ConnectionCloseTimeout">Native connection abort window, subject to client minimums.</param>
public sealed record RabbitMqSessionOptions(
    TimeSpan ProtocolTimeout,
    TimeSpan ConnectionCloseTimeout
);
