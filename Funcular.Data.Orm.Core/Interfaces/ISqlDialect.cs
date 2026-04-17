using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Text;
using Funcular.Data.Orm.Attributes;

namespace Funcular.Data.Orm.Interfaces
{
    /// <summary>
    /// Defines the contract for generating SQL statements for a specific database dialect.
    /// </summary>
    public interface ISqlDialect
    {
        /// <summary>
        /// Encloses the identifier in dialect-specific delimiters (e.g., [Name] for SQL Server, "Name" for Postgres).
        /// </summary>
        string EncloseIdentifier(string identifier);

        /// <summary>
        /// Checks if the word is a reserved keyword in the dialect.
        /// </summary>
        bool IsReservedWord(string word);

        /// <summary>
        /// Builds an INSERT command text and parameters for the specified entity.
        /// </summary>
        (string CommandText, IEnumerable<IDbDataParameter> Parameters) BuildInsertCommand<T>(
            T entity, 
            string tableName, 
            PropertyInfo primaryKey, 
            Func<PropertyInfo, string> getColumnName, 
            Func<Type, object> getDefaultValue,
            IEnumerable<PropertyInfo> properties) where T : class;

        /// <summary>
        /// Builds an UPDATE command text and parameters for the specified entity.
        /// </summary>
        (string CommandText, IEnumerable<IDbDataParameter> Parameters) BuildUpdateCommand<T>(
            T entity, 
            T existing, 
            string tableName, 
            PropertyInfo primaryKey, 
            Func<PropertyInfo, string> getColumnName, 
            IEnumerable<PropertyInfo> properties) where T : class;

        /// <summary>
        /// Builds a DELETE command text.
        /// </summary>
        string BuildDeleteCommand(string tableName, string whereClause);

        /// <summary>
        /// Builds a SELECT command text.
        /// </summary>
        string BuildSelectCommand(string tableName, string columnNames, string whereClause, string joinClauses = null);

        /// <summary>
        /// Builds a SQL expression that extracts a scalar value from a JSON column using a JSON path.
        /// For SQL Server this produces <c>JSON_VALUE(column, '$.path')</c>.
        /// For PostgreSQL this produces <c>column #&gt;&gt; '{path}'</c>.
        /// When <paramref name="castType"/> is specified, the result is wrapped in a CAST or <c>::type</c>.
        /// </summary>
        /// <param name="qualifiedColumn">The fully qualified column expression (e.g., <c>[project].[metadata]</c>).</param>
        /// <param name="jsonPath">The JSON path expression (e.g., <c>$.client.name</c>).</param>
        /// <param name="castType">Optional SQL type to cast the result to (e.g., <c>"int"</c>). Null for no cast.</param>
        /// <returns>A SQL expression string that extracts the JSON value.</returns>
        string BuildJsonValueExpression(string qualifiedColumn, string jsonPath, string castType = null);

        /// <summary>
        /// Gets the provider name used for selecting provider-specific <see cref="SqlExpressionAttribute"/> expressions.
        /// Returns "mssql" or "postgresql".
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Builds a correlated scalar subquery for an aggregate operation on a child table.
        /// </summary>
        /// <param name="childTableName">The child table name (already resolved, e.g., <c>project_milestone</c>).</param>
        /// <param name="childFkColumn">The foreign key column on the child table (e.g., <c>project_id</c>).</param>
        /// <param name="parentPkExpression">The parent primary key expression (e.g., <c>project.id</c>).</param>
        /// <param name="function">The aggregate function to apply.</param>
        /// <param name="aggregateColumn">Column to aggregate for Sum/Avg. Null for Count/ConditionalCount.</param>
        /// <param name="conditionColumn">Column for conditional filter. Null for non-conditional aggregates.</param>
        /// <param name="conditionValue">Value for conditional filter. Null for non-conditional aggregates.</param>
        /// <returns>A complete correlated scalar subquery expression (e.g., <c>(SELECT COUNT(*) FROM ... WHERE ...)</c>).</returns>
        string BuildScalarSubquery(string childTableName, string childFkColumn, string parentPkExpression,
            AggregateFunction function, string aggregateColumn = null,
            string conditionColumn = null, string conditionValue = null);

        /// <summary>
        /// Builds a correlated subquery that projects child records as a JSON array.
        /// For SQL Server generates <c>(SELECT ... FOR JSON PATH)</c>.
        /// For PostgreSQL generates <c>(SELECT json_agg(row_to_json(sub)) FROM (...) sub)</c>.
        /// </summary>
        /// <param name="childTableName">The child table name.</param>
        /// <param name="childFkColumn">The foreign key column on the child table.</param>
        /// <param name="parentPkExpression">The parent primary key expression.</param>
        /// <param name="columnExpressions">List of column expressions to include (already resolved to SQL column names).</param>
        /// <param name="orderByColumn">Column to order by. Null for no ordering.</param>
        /// <returns>A complete correlated subquery expression producing a JSON array string.</returns>
        string BuildJsonCollectionSubquery(string childTableName, string childFkColumn, string parentPkExpression,
            IList<string> columnExpressions, string orderByColumn = null);
    }
}
