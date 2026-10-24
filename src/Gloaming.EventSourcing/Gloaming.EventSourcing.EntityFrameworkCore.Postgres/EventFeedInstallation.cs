using System.Globalization;
using System.Text;

namespace Gloaming.EventSourcing.EntityFrameworkCore.Postgres;

/// <summary>Produces reviewed, one-time SQL for a consumer-owned native migration, never startup DDL.</summary>
public static class EventFeedInstallation
{
    /// <summary>
    /// Locks all source tables, copies a synthetic historical baseline and installs atomic capture.
    /// Execute the complete script in one migration transaction with source writers paused.
    /// The migration role owns the security-definer functions; runtime roles must not own these objects.
    /// </summary>
    public static string Create(EventFeedLocation location, IEnumerable<EventFeedSource> sources)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(sources);
        var selected = sources
            .OrderBy(x => x.Schema, StringComparer.Ordinal)
            .ThenBy(x => x.Table, StringComparer.Ordinal)
            .ToArray();
        if (
            selected.Length is < 1 or > 32
            || selected.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count()
                != selected.Length
            || selected.Select(x => (x.Schema, x.Table)).Distinct().Count() != selected.Length
        )
            throw new ArgumentException("Select one to32 distinct source names and tables.");

        string head = location.Table("feed_head");
        string entries = location.Table("feed_entries");
        string immutable = location.Table("reject_event_mutation");
        string checkpoints = location.Table("projection_checkpoints");
        var sql = new StringBuilder();
        sql.AppendLine(
            "LOCK TABLE "
                + string.Join(", ", selected.Select(SourceTable))
                + " IN ACCESS EXCLUSIVE MODE;"
        );
        sql.AppendLine(
            CultureInfo.InvariantCulture,
            $"""
            CREATE TABLE {head} (
                singleton boolean PRIMARY KEY DEFAULT true CHECK(singleton),
                protocol integer NOT NULL CHECK(protocol = 1), feed_id uuid NOT NULL,
                position bigint NOT NULL CHECK(position >= 0), last_transaction xid8 NOT NULL
            );
            INSERT INTO {head} VALUES(true, 1, '{location.FeedId:D}', 1, pg_catalog.pg_current_xact_id());
            CREATE TABLE {entries} (
                position bigint NOT NULL CHECK(position > 0), source text COLLATE "C" NOT NULL,
                stream_key jsonb NOT NULL CHECK(pg_catalog.jsonb_typeof(stream_key) = 'array' AND pg_catalog.pg_column_size(stream_key) <= 512),
                version bigint NOT NULL CHECK(version > 0),
                event_key jsonb NOT NULL CHECK(pg_catalog.jsonb_typeof(event_key) = 'array' AND pg_catalog.pg_column_size(event_key) <= 512),
                stream_sort text COLLATE "C" GENERATED ALWAYS AS (stream_key::text) STORED,
                event_sort text COLLATE "C" GENERATED ALWAYS AS (event_key::text) STORED,
                envelope jsonb NOT NULL CHECK(pg_catalog.jsonb_typeof(envelope) = 'object'),
                PRIMARY KEY(position, source, stream_sort, version, event_sort),
                UNIQUE(source, event_sort), UNIQUE(source, stream_sort, version)
            );
            CREATE TABLE {checkpoints} (
                name text COLLATE "C" NOT NULL, generation text COLLATE "C" NOT NULL,
                definition text COLLATE "C" NOT NULL, cursor text NOT NULL,
                PRIMARY KEY(name, generation)
            );
            CREATE TABLE {location.Table("projection_active")} (
                name text COLLATE "C" PRIMARY KEY, generation text COLLATE "C" NOT NULL,
                FOREIGN KEY(name, generation) REFERENCES {checkpoints}(name, generation) ON DELETE RESTRICT
            );
            CREATE FUNCTION {immutable}() RETURNS trigger LANGUAGE plpgsql
            SET search_path = pg_catalog, pg_temp AS $g$
            BEGIN RAISE EXCEPTION 'Retained feed sources are append-only'; END $g$;
            REVOKE ALL ON FUNCTION {immutable}() FROM PUBLIC;
            """
        );

        foreach (var source in selected)
        {
            string table = SourceTable(source);
            string stream = Key(source.StreamKeyColumns, "r");
            string eventKey = Key(source.EventKeyColumns, "r");
            string version = "r." + EventFeedLocation.Quote(source.VersionColumn);
            string nativeName = Literal(source.Name);
            string tableLiteral = Literal(table);
            string columns = string.Join(
                ", ",
                source
                    .StreamKeyColumns.Concat(source.EventKeyColumns)
                    .Append(source.VersionColumn)
                    .Distinct(StringComparer.Ordinal)
                    .Select(Literal)
            );
            string pk = string.Join(", ", source.EventKeyColumns.Select(Literal));
            sql.AppendLine(
                CultureInfo.InvariantCulture,
                $"""
                DO $g$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class c
                        WHERE c.oid = {tableLiteral}::pg_catalog.regclass AND c.relkind = 'r'
                            AND NOT c.relispartition AND NOT c.relrowsecurity
                            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = c.oid OR i.inhparent = c.oid)
                            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = c.oid)
                    ) THEN RAISE EXCEPTION 'Feed source requires an ordinary table without inheritance, RLS or rewrite rules'; END IF;
                    IF EXISTS (
                        SELECT 1 FROM pg_catalog.unnest(ARRAY[{columns}]::text[]) c
                        LEFT JOIN pg_catalog.pg_attribute a ON a.attrelid = {tableLiteral}::pg_catalog.regclass AND a.attname = c
                        WHERE a.attnum IS NULL OR a.attnum <= 0 OR a.attisdropped OR NOT a.attnotnull
                            OR a.atttypid NOT IN ('text'::pg_catalog.regtype, 'uuid'::pg_catalog.regtype,
                                'integer'::pg_catalog.regtype, 'bigint'::pg_catalog.regtype, 'smallint'::pg_catalog.regtype)
                    ) THEN RAISE EXCEPTION 'Feed keys require explicit nonnullable scalar native columns'; END IF;
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_constraint p
                        WHERE p.conrelid = {tableLiteral}::pg_catalog.regclass AND p.contype = 'p'
                        AND (SELECT pg_catalog.array_agg(a.attname::text ORDER BY k.n) FROM pg_catalog.unnest(p.conkey) WITH ORDINALITY k(id,n)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = p.conrelid AND a.attnum = k.id) = ARRAY[{pk}]::text[]
                    ) THEN RAISE EXCEPTION 'Feed event key must be the complete native primary key in declared order'; END IF;
                    IF EXISTS (SELECT 1 FROM {table} r GROUP BY {stream}
                        HAVING min({version}) <> 1 OR max({version}) <> count(*) OR count(DISTINCT {version}) <> count(*))
                    THEN RAISE EXCEPTION 'Feed baseline requires contiguous positive per-stream history'; END IF;
                END $g$;
                INSERT INTO {entries}(position, source, stream_key, version, event_key, envelope)
                SELECT 1, {nativeName}, {stream}, {version}, {eventKey}, pg_catalog.to_jsonb(r) FROM {table} r;
                """
            );

            // A distinct function per explicit binding avoids dynamically interpreted column expressions.
            string function = location.Table(
                "capture_" + Array.IndexOf(selected, source).ToString(CultureInfo.InvariantCulture)
            );
            string newStream = Key(source.StreamKeyColumns, "NEW");
            string newEvent = Key(source.EventKeyColumns, "NEW");
            sql.AppendLine(
                CultureInfo.InvariantCulture,
                $"""
                CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
                SET search_path = pg_catalog, pg_temp AS $g$
                DECLARE batch bigint;
                BEGIN
                    UPDATE {head} SET position = CASE WHEN last_transaction = pg_catalog.pg_current_xact_id()
                            THEN position ELSE position + 1 END,
                        last_transaction = pg_catalog.pg_current_xact_id()
                    WHERE singleton RETURNING position INTO STRICT batch;
                    INSERT INTO {entries}(position, source, stream_key, version, event_key, envelope)
                    VALUES(batch, {nativeName}, {newStream},
                        NEW.{EventFeedLocation.Quote(
                    source.VersionColumn
                )}, {newEvent}, pg_catalog.to_jsonb(NEW));
                    RETURN NEW;
                END $g$;
                REVOKE ALL ON FUNCTION {function}() FROM PUBLIC;
                CREATE TRIGGER gloaming_feed_capture AFTER INSERT ON {table}
                    FOR EACH ROW EXECUTE FUNCTION {function}();
                CREATE TRIGGER gloaming_feed_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON {table}
                    FOR EACH STATEMENT EXECUTE FUNCTION {immutable}();
                """
            );
        }

        // No captured rows means Start can immediately advance past the synthetic empty baseline.
        return sql.ToString();
    }

    private static string SourceTable(EventFeedSource source) =>
        EventFeedLocation.Quote(source.Schema) + "." + EventFeedLocation.Quote(source.Table);

    private static string Key(IEnumerable<string> columns, string row) =>
        "pg_catalog.jsonb_build_array("
        + string.Join(", ", columns.Select(c => row + "." + EventFeedLocation.Quote(c)))
        + ")";

    private static string Literal(string value) => "'" + value.Replace("'", "''") + "'";
}
