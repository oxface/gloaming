using System.Text;

namespace Gloaming.EventSourcing.EntityFrameworkCore.Postgres;

/// <summary>One fixed feed protocol in a consumer-owned PostgreSQL schema.</summary>
public sealed class EventFeedLocation
{
    /// <summary>Selects a trusted native schema and immutable nonempty feed identity.</summary>
    public EventFeedLocation(string schema, Guid feedId)
    {
        Identifier(schema);
        ArgumentOutOfRangeException.ThrowIfEqual(feedId, Guid.Empty);

        Schema = schema;
        FeedId = feedId;
    }

    /// <summary>Consumer-created trusted native schema.</summary>
    public string Schema { get; }

    /// <summary>Immutable identity retained in the installed head and every cursor.</summary>
    public Guid FeedId { get; }

    internal string Table(string name) => $"{Quote(Schema)}.{Quote(name)}";

    internal static string Quote(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";

    internal static void Identifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (
            Encoding.UTF8.GetByteCount(value) > 63
            || !(char.IsAsciiLetter(value[0]) || value[0] == '_')
            || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')
        )
            throw new ArgumentException(
                "Use an ASCII letter/underscore identifier followed by letters, digits or underscores, at most63 bytes."
            );
    }
}

/// <summary>Explicit native append-only source binding. Keys contain every ownership component.</summary>
public sealed class EventFeedSource
{
    /// <summary>Freezes explicit ordinary-table columns; installation verifies their native shape.</summary>
    public EventFeedSource(
        string name,
        string schema,
        string table,
        IEnumerable<string> streamKeyColumns,
        string versionColumn,
        IEnumerable<string> eventKeyColumns
    )
    {
        EventFeedLocation.Identifier(name);
        EventFeedLocation.Identifier(schema);
        EventFeedLocation.Identifier(table);
        EventFeedLocation.Identifier(versionColumn);

        Name = name;
        Schema = schema;
        Table = table;
        StreamKeyColumns = Columns(streamKeyColumns);
        VersionColumn = versionColumn;
        EventKeyColumns = Columns(eventKeyColumns);
    }

    /// <summary>Stable ordinal source identity retained in captured entries.</summary>
    public string Name { get; }

    /// <summary>Native source schema.</summary>
    public string Schema { get; }

    /// <summary>Native ordinary append-only source table.</summary>
    public string Table { get; }

    /// <summary>Complete ordered native stream key, including ownership columns.</summary>
    public IReadOnlyList<string> StreamKeyColumns { get; }

    /// <summary>Positive integer stream version column.</summary>
    public string VersionColumn { get; }

    /// <summary>Complete native primary-key columns in primary-key order.</summary>
    public IReadOnlyList<string> EventKeyColumns { get; }

    private static System.Collections.ObjectModel.ReadOnlyCollection<string> Columns(
        IEnumerable<string> columns
    )
    {
        ArgumentNullException.ThrowIfNull(columns);
        string[] copy = columns.ToArray();
        if (
            copy.Length is < 1 or > 8
            || copy.Distinct(StringComparer.Ordinal).Count() != copy.Length
        )
            throw new ArgumentException("Use one to eight distinct native key columns.");

        foreach (string column in copy)
            EventFeedLocation.Identifier(column);

        return Array.AsReadOnly(copy);
    }
}
