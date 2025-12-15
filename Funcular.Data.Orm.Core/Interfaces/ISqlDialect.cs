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
    }
}
