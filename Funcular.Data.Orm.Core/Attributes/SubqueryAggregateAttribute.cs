using System;

namespace Funcular.Data.Orm.Attributes
{
    /// <summary>
    /// Decorates a property to indicate its value is computed from a correlated scalar subquery
    /// against a child table. Generates <c>(SELECT aggregate FROM child WHERE child.fk = parent.pk)</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// // Simple count:
    /// [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
    ///     AggregateFunction.Count)]
    /// public int MilestoneCount { get; set; }
    ///
    /// // Conditional count:
    /// [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
    ///     AggregateFunction.ConditionalCount,
    ///     ConditionColumn = nameof(ProjectMilestoneEntity.Status),
    ///     ConditionValue = "completed")]
    /// public int MilestonesCompleted { get; set; }
    ///
    /// // Sum:
    /// [SubqueryAggregate(typeof(OrderLineEntity), nameof(OrderLineEntity.OrderId),
    ///     AggregateFunction.Sum,
    ///     AggregateColumn = nameof(OrderLineEntity.Amount))]
    /// public decimal OrderTotal { get; set; }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class SubqueryAggregateAttribute : Attribute
    {
        /// <summary>
        /// Gets the child entity type to aggregate over (e.g., <c>typeof(ProjectMilestoneEntity)</c>).
        /// </summary>
        public Type SourceType { get; }

        /// <summary>
        /// Gets the property name on the child entity that references the parent's primary key
        /// (e.g., <c>nameof(ProjectMilestoneEntity.ProjectId)</c>).
        /// </summary>
        public string ForeignKey { get; }

        /// <summary>
        /// Gets the aggregate function to apply.
        /// </summary>
        public AggregateFunction Function { get; }

        /// <summary>
        /// Gets or sets the column to aggregate for <see cref="AggregateFunction.Sum"/> 
        /// and <see cref="AggregateFunction.Avg"/>. Not used for <c>Count</c> or <c>ConditionalCount</c>.
        /// </summary>
        public string AggregateColumn { get; set; }

        /// <summary>
        /// Gets or sets the column name on the child entity to filter on (for conditional aggregates).
        /// Used with <see cref="AggregateFunction.ConditionalCount"/>.
        /// </summary>
        public string ConditionColumn { get; set; }

        /// <summary>
        /// Gets or sets the value to match in the condition column (for conditional aggregates).
        /// Used with <see cref="AggregateFunction.ConditionalCount"/>.
        /// </summary>
        public string ConditionValue { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SubqueryAggregateAttribute"/> class.
        /// </summary>
        /// <param name="sourceType">The child entity type.</param>
        /// <param name="foreignKey">The FK property name on the child that references the parent PK.</param>
        /// <param name="function">The aggregate function.</param>
        public SubqueryAggregateAttribute(Type sourceType, string foreignKey, AggregateFunction function)
        {
            SourceType = sourceType ?? throw new ArgumentNullException(nameof(sourceType));
            ForeignKey = foreignKey ?? throw new ArgumentNullException(nameof(foreignKey));
            Function = function;
        }
    }
}
