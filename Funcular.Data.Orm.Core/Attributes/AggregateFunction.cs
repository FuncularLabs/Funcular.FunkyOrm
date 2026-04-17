namespace Funcular.Data.Orm.Attributes
{
    /// <summary>
    /// Specifies the aggregate function to use with <see cref="SubqueryAggregateAttribute"/>.
    /// </summary>
    public enum AggregateFunction
    {
        /// <summary>
        /// COUNT(*) — counts all rows matching the foreign key.
        /// </summary>
        Count,

        /// <summary>
        /// SUM(column) — sums a numeric column. Requires <see cref="SubqueryAggregateAttribute.AggregateColumn"/>.
        /// </summary>
        Sum,

        /// <summary>
        /// AVG(column) — averages a numeric column. Requires <see cref="SubqueryAggregateAttribute.AggregateColumn"/>.
        /// </summary>
        Avg,

        /// <summary>
        /// COUNT(*) with an additional equality condition on a column.
        /// Requires <see cref="SubqueryAggregateAttribute.ConditionColumn"/> and
        /// <see cref="SubqueryAggregateAttribute.ConditionValue"/>.
        /// Generates: <c>COUNT(*) ... WHERE fk = parent.id AND condition_column = 'value'</c>.
        /// </summary>
        ConditionalCount
    }
}
