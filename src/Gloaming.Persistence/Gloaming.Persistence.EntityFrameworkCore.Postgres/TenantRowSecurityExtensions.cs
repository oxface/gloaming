using System.Runtime.CompilerServices;
using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Gloaming.Persistence.EntityFrameworkCore.Postgres;

public static class TenantRowSecurityExtensions
{
    internal const string Capability = "postgres-tenant-rls";
    internal const string SettingAnnotation = "Gloaming:PostgresTenantRlsSetting";
    private const string Policy = "gloaming_tenant";
    private static readonly ConditionalWeakTable<IModel, Selection> Selections = new();

    private sealed record Selection(string? Setting);

    /// <summary>Selects automatic transaction-local context for an explicitly tenant-owned text table.
    /// Policies must also be deployed through reviewed migrations.</summary>
    public static EntityTypeBuilder<TEntity> WithRls<TEntity>(
        this EntityTypeBuilder<TEntity> entity,
        string settingName
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ValidateSetting(settingName);
        ValidateOwnership(entity.Metadata);
        if (entity.Metadata.FindAnnotation(SettingAnnotation) is not null)
            throw new InvalidOperationException("RLS is already declared for this entity.");

        if (
            entity
                .Metadata.Model.GetEntityTypes()
                .Any(other =>
                    other.FindAnnotation(SettingAnnotation)?.Value is string existing
                    && existing != settingName
                )
        )
            throw new InvalidOperationException(
                "One DbContext must use one authoritative RLS setting."
            );

        entity.HasAnnotation(SettingAnnotation, settingName);
        entity.Metadata.Model.RequireGloamingCapability(Capability);
        return entity;
    }

    /// <summary>Emits explicit versioned read/write policies; does not grant runtime privileges.</summary>
    public static MigrationBuilder EnableTenantRowSecurity(
        this MigrationBuilder migration,
        string table,
        string tenantColumn,
        string settingName,
        string? schema = null
    )
    {
        ArgumentNullException.ThrowIfNull(migration);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantColumn);
        ValidateSetting(settingName);

        string target = Table(table, schema);
        using var identifiers = new NpgsqlCommandBuilder();
        string predicate =
            $"{identifiers.QuoteIdentifier(tenantColumn)} COLLATE \"C\" = NULLIF(current_setting('{settingName}', true), '')";
        migration.Sql(
            $"""
            ALTER TABLE {target} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {target} FORCE ROW LEVEL SECURITY;
            CREATE POLICY {Policy} ON {target} FOR ALL TO PUBLIC
                USING ({predicate}) WITH CHECK ({predicate});
            """
        );
        return migration;
    }

    /// <summary>Reverses this policy and its table flags. Review other policies before disabling RLS.</summary>
    public static MigrationBuilder DisableTenantRowSecurity(
        this MigrationBuilder migration,
        string table,
        string? schema = null
    )
    {
        ArgumentNullException.ThrowIfNull(migration);

        string target = Table(table, schema);
        migration.Sql(
            $"""
            DROP POLICY {Policy} ON {target};
            ALTER TABLE {target} NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE {target} DISABLE ROW LEVEL SECURITY;
            """
        );
        return migration;
    }

    internal static string? Setting(IModel model) =>
        Selections.GetValue(model, value => new Selection(ValidateModel(value))).Setting;

    private static string? ValidateModel(IModel model)
    {
        string? setting = null;
        foreach (var entity in model.GetEntityTypes())
        {
            if (entity.FindAnnotation(SettingAnnotation)?.Value is not string selected)
                continue;

            ValidateOwnership(entity, runtime: true);
            ValidateSetting(selected);
            if (setting is not null && setting != selected)
                throw new InvalidOperationException(
                    "One DbContext must use one authoritative RLS setting."
                );

            setting = selected;
        }

        return setting;
    }

    private static void ValidateOwnership(IReadOnlyEntityType entity, bool runtime = false)
    {
        var property = entity.FindTenantOwnershipProperty();
        string? table = entity.GetTableName();
        if (
            property is null
            || property.ClrType != typeof(string)
            || property.IsNullable
            || !property.IsConcurrencyToken
            || property.ValueGenerated != ValueGenerated.Never
            || property.GetValueConverter() is not null
            || property.GetProviderClrType() is not null
            || table is null
            || entity.GetViewName() is not null
            || entity.BaseType is not null
            || entity.GetDerivedTypes().Any()
            || entity.IsOwned()
            || entity.GetComplexProperties().Any()
            || entity.GetMappingFragments().Any()
            || entity
                .Model.GetEntityTypes()
                .Any(other =>
                    other != entity
                    && other.GetTableName() == table
                    && other.GetSchema() == entity.GetSchema()
                )
        )
            throw new InvalidOperationException(
                "RLS requires HasTenantOwnership on an ordinary, unshared text-key table without converters."
            );

        if (
            runtime
            && (
                property.GetTypeMapping().Converter is not null
                || property.GetColumnType() != "text"
            )
        )
            throw new InvalidOperationException(
                "RLS currently supports only unconverted PostgreSQL text discriminators."
            );
    }

    private static string Table(string table, string? schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        if (schema is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        using var identifiers = new NpgsqlCommandBuilder();
        return (schema is null ? "" : identifiers.QuoteIdentifier(schema) + ".")
            + identifiers.QuoteIdentifier(table);
    }

    private static void ValidateSetting(string setting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);
        var parts = setting.Split('.');
        if (
            parts.Length < 2
            || parts.Any(part =>
                part.Length == 0
                || !(char.IsAsciiLetterLower(part[0]) || part[0] == '_')
                || part.Any(character =>
                    !(
                        char.IsAsciiLetterLower(character)
                        || char.IsAsciiDigit(character)
                        || character == '_'
                    )
                )
            )
        )
            throw new ArgumentException(
                "Use a qualified lowercase PostgreSQL custom setting name.",
                nameof(setting)
            );
    }
}
