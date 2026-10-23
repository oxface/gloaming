using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

internal static class StreamKeyPredicate
{
    internal static Expression<Func<TRecord, bool>> Create<TRecord>(
        IReadOnlyList<IProperty> properties,
        IReadOnlyList<object?> values
    )
    {
        var parameter = Expression.Parameter(typeof(TRecord), "record");
        Expression predicate = Expression.Constant(true);
        for (int index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            // Values vary between reads without becoming part of the compiled query shape.
            var keyParameter = Expression.Call(
                typeof(EF),
                nameof(EF.Parameter),
                [property.ClrType],
                Expression.Constant(values[index], property.ClrType)
            );
            predicate = Expression.AndAlso(
                predicate,
                Expression.Equal(
                    Expression.Property(parameter, property.PropertyInfo!),
                    keyParameter
                )
            );
        }

        return Expression.Lambda<Func<TRecord, bool>>(predicate, parameter);
    }
}
