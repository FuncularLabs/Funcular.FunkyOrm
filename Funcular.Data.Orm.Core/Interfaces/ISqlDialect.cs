using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Text;

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
    }
}
