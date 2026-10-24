using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

internal sealed class ContributionStorage
{
    internal string SetsTable { get; }

    internal ContributionStorage(DbContext database)
    {
        _ = new DueIntentStorage(database);
        string[] key =
        [
            nameof(ContributionSetRecord.ScopeKey),
            nameof(ContributionSetRecord.WorkId),
            nameof(ContributionSetRecord.GenerationId),
        ];
        string[] setNames =
        [
            .. key,
            nameof(ContributionSetRecord.Expected),
            nameof(ContributionSetRecord.Completion),
            nameof(ContributionSetRecord.DueAt),
            nameof(ContributionSetRecord.TimeoutSignal),
            nameof(ContributionSetRecord.TimeoutOutcome),
            nameof(ContributionSetRecord.Revision),
            nameof(ContributionSetRecord.State),
        ];
        string[] setColumns =
        [
            "scope_key",
            "work_id",
            "generation_id",
            "expected",
            "completion",
            "due_at",
            "timeout_signal",
            "timeout_outcome",
            "revision",
            "state",
        ];
        var set = Model(database, typeof(ContributionSetRecord), setNames, setColumns, key);
        string[] contributionNames =
        [
            .. key,
            nameof(ContributionRecord.ContributorId),
            nameof(ContributionRecord.Name),
            nameof(ContributionRecord.SchemaVersion),
            nameof(ContributionRecord.Payload),
        ];
        var content = Model(
            database,
            typeof(ContributionRecord),
            contributionNames,
            [
                "scope_key",
                "work_id",
                "generation_id",
                "contributor_id",
                "name",
                "schema_version",
                "payload",
            ],
            [.. key, nameof(ContributionRecord.ContributorId)]
        );
        if (
            set.FindProperty(nameof(ContributionSetRecord.Revision))?.IsConcurrencyToken is not true
            || set.FindProperty(nameof(ContributionSetRecord.Revision))?.GetColumnType() != "bigint"
            || set.FindProperty(nameof(ContributionSetRecord.State))?.GetColumnType() != "integer"
            || set.FindProperty(nameof(ContributionSetRecord.DueAt))?.GetColumnType()
                is not ("timestamp with time zone" or "timestamp(6) with time zone")
            || set.FindProperty(nameof(ContributionSetRecord.DueAt))?.GetPrecision() != 6
            || new[]
            {
                nameof(ContributionSetRecord.Expected),
                nameof(ContributionSetRecord.Completion),
                nameof(ContributionSetRecord.TimeoutSignal),
                nameof(ContributionSetRecord.TimeoutOutcome),
            }.Any(name => set.FindProperty(name)?.GetColumnType() != "jsonb")
            || content.FindProperty(nameof(ContributionRecord.Payload))?.GetColumnType() != "jsonb"
            || content.FindProperty(nameof(ContributionRecord.ContributorId))?.GetMaxLength() != 256
            || database
                .GetService<IDesignTimeModel>()
                .Model.FindEntityType(typeof(ContributionRecord))!
                .FindProperty(nameof(ContributionRecord.ContributorId))
                ?.GetCollation() != "C"
            || !content
                .GetForeignKeys()
                .Any(foreign =>
                    foreign.PrincipalEntityType.ClrType == typeof(ContributionSetRecord)
                    && foreign.Properties.Select(property => property.Name).SequenceEqual(key)
                    && foreign
                        .PrincipalKey.Properties.Select(property => property.Name)
                        .SequenceEqual(key)
                    && foreign.DeleteBehavior == DeleteBehavior.Restrict
                )
            || database
                .Model.FindEntityType(typeof(DueIntentRecord))!
                .GetDeclaredQueryFilters()
                .Count != 0
            || database
                .Model.FindEntityType(typeof(OutboxMessageRecord))!
                .GetDeclaredQueryFilters()
                .Count != 0
        )
            throw new InvalidOperationException(
                "Retain the fixed unfiltered contribution model, immutable native content and generation concurrency mapping."
            );

        SetsTable = database
            .GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(set.GetTableName()!, set.GetSchema());
    }

    private static IEntityType Model(
        DbContext database,
        Type type,
        string[] names,
        string[] columns,
        string[] key
    )
    {
        var entity = database.Model.FindEntityType(type);
        if (
            entity?.FindAnnotation(PostgresContributionConfiguration.Capability)?.Value is not true
            || entity.GetTableName() is not { } table
            || entity.GetSchema() is not { } schema
        )
            throw new InvalidOperationException(
                "ConfigurePostgresContributions must map both provided records."
            );

        PostgresDueIntentConfiguration.ValidateIdentifier(schema);
        PostgresDueIntentConfiguration.ValidateIdentifier(table);
        var store = StoreObjectIdentifier.Table(table, schema);
        if (
            entity.BaseType is not null
            || entity.IsOwned()
            || entity.GetDerivedTypes().Any()
            || entity.GetMappingFragments().Any()
            || entity.GetViewName() is not null
            || entity.GetDeclaredQueryFilters().Count != 0
            || entity.GetProperties().Count() != names.Length
            || names
                .Where(
                    (name, index) =>
                        entity.FindProperty(name)?.GetColumnName(store) != columns[index]
                )
                .Any()
            || entity
                .FindPrimaryKey()
                ?.Properties.Select(property => property.Name)
                .SequenceEqual(key)
                is not true
            || entity
                .GetProperties()
                .Any(property =>
                    property.IsShadowProperty()
                    || property.GetValueConverter() is not null
                    || property.GetProviderClrType() is not null
                    || property.ValueGenerated != ValueGenerated.Never
                    || property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save
                    || property.GetAfterSaveBehavior() == PropertySaveBehavior.Ignore
                )
            || entity.FindProperty(nameof(ContributionSetRecord.ScopeKey))?.GetMaxLength() != 256
            || database
                .GetService<IDesignTimeModel>()
                .Model.FindEntityType(type)!
                .FindProperty(nameof(ContributionSetRecord.ScopeKey))
                ?.GetCollation() != "C"
            || database
                .Model.GetEntityTypes()
                .Any(other =>
                    other.ClrType != type
                    && other.GetTableName() == table
                    && other.GetSchema() == schema
                )
        )
            throw new InvalidOperationException(
                "Retain independent fixed contribution tables, complete ordinal keys, native scalar mappings and unfiltered storage."
            );

        return entity;
    }
}
