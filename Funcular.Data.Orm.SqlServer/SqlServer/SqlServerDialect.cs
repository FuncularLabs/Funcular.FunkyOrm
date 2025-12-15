using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using Funcular.Data.Orm.Interfaces;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    /// <summary>
    /// SQL Server implementation of the <see cref="ISqlDialect"/>.
    /// </summary>
    public class SqlServerDialect : ISqlDialect
    {
        private static readonly HashSet<string> _reservedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "AUTHORIZATION", "BACKUP", "BEGIN", "BETWEEN", "BREAK", "BROWSE", "BULK", "BY", "CASCADE", "CASE", "CHECK", "CHECKPOINT", "CLOSE", "CLUSTERED", "COALESCE", "COLLATE", "COLUMN", "COMMIT", "COMPUTE", "CONSTRAINT", "CONTAINS", "CONTAINSTABLE", "CONTINUE", "CONVERT", "CREATE", "CROSS", "CURRENT", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP", "CURRENT_USER", "CURSOR", "DATABASE", "DBCC", "DEALLOCATE", "DECLARE", "DEFAULT", "DELETE", "DENY", "DESC", "DISK", "DISTINCT", "DISTRIBUTED", "DOUBLE", "DROP", "DUMP", "ELSE", "END", "ERRLVL", "ESCAPE", "EXCEPT", "EXEC", "EXECUTE", "EXISTS", "EXIT", "EXTERNAL", "FETCH", "FILE", "FILLFACTOR", "FOR", "FOREIGN", "FREETEXT", "FREETEXTTABLE", "FROM", "FULL", "FUNCTION", "GOTO", "GRANT", "GROUP", "HAVING", "HOLDLOCK", "IDENTITY", "IDENTITY_INSERT", "IDENTITYCOL", "IF", "IN", "INDEX", "INNER", "INSERT", "INTERSECT", "INTO", "IS", "JOIN", "KEY", "KILL", "LEFT", "LIKE", "LINENO", "LOAD", "MERGE", "NATIONAL", "NOCHECK", "NONCLUSTERED", "NOT", "NULL", "NULLIF", "OF", "OFF", "OFFSETS", "ON", "OPEN", "OPENDATASOURCE", "OPENQUERY", "OPENROWSET", "OPENXML", "OPTION", "OR", "ORDER", "OUTER", "OVER", "PERCENT", "PIVOT", "PLAN", "PRECISION", "PRIMARY", "PRINT", "PROC", "PROCEDURE", "PUBLIC", "RAISERROR", "READ", "READTEXT", "RECONFIGURE", "REFERENCES", "REPLICATION", "RESTORE", "RESTRICT", "RETURN", "REVERT", "REVOKE", "RIGHT", "ROLLBACK", "ROWCOUNT", "ROWGUIDCOL", "RULE", "SAVE", "SCHEMA", "SECURITYAUDIT", "SELECT", "SEMANTICKEYPHRASETABLE", "SEMANTICSIMILARITYDETAILSTABLE", "SEMANTICSIMILARITYTABLE", "SESSION_USER", "SET", "SETUSER", "SHUTDOWN", "SOME", "STATISTICS", "SYSTEM_USER", "TABLE", "TABLESAMPLE", "TEXTSIZE", "THEN", "TO", "TOP", "TRAN", "TRANSACTION", "TRIGGER", "TRUNCATE", "TRY_CONVERT", "TSEQUAL", "UNION", "UNIQUE", "UNPIVOT", "UPDATE", "UPDATETEXT", "USE", "USER", "VALUES", "VARYING", "VIEW", "WAITFOR", "WHEN", "WHERE", "WHILE", "WITH", "WITHIN GROUP", "WRITETEXT"
        };

        public string EncloseIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return identifier;
            if (identifier.StartsWith("[") && identifier.EndsWith("]")) return identifier;
            return IsReservedWord(identifier) ? $"[{identifier}]" : identifier;
        }

        public bool IsReservedWord(string word)
        {
            return _reservedWords.Contains(word?.ToUpperInvariant());
        }

        public (string CommandText, IEnumerable<IDbDataParameter> Parameters) BuildInsertCommand<T>(
            T entity, 
            string tableName, 
            PropertyInfo primaryKey, 
            Func<PropertyInfo, string> getColumnName, 
            Func<Type, object> getDefaultValue,
            IEnumerable<PropertyInfo> properties) where T : class
        {
            // Determine if we should include the primary key in the insert.
            bool includePrimaryKey = false;
            if (primaryKey.PropertyType != typeof(int) && primaryKey.PropertyType != typeof(long))
            {
                var pkValue = primaryKey.GetValue(entity);
                var defaultValue = getDefaultValue(primaryKey.PropertyType);
                if (!Equals(pkValue, defaultValue))
                {
                    includePrimaryKey = true;
                }
            }

            var includedProperties = properties
                .Where(p => p != primaryKey || includePrimaryKey);
            
            var parameters = new List<SqlParameter>();
            var columnNames = new List<string>();
            var parameterNames = new List<string>();

            foreach (var property in includedProperties)
            {
                var columnName = getColumnName(property);
                var value = property.GetValue(entity);
                var parameterName = $"@{property.Name}";

                columnNames.Add(columnName);
                parameterNames.Add(parameterName);
                parameters.Add(CreateParameter(parameterName, value, property.PropertyType));
            }

            var commandText = $@"
                INSERT INTO {tableName} ({string.Join(", ", columnNames)})
                OUTPUT INSERTED.{getColumnName(primaryKey)}
                VALUES ({string.Join(", ", parameterNames)})";
            
            return (commandText, parameters);
        }

        public (string CommandText, IEnumerable<IDbDataParameter> Parameters) BuildUpdateCommand<T>(
            T entity, 
            T existing, 
            string tableName, 
            PropertyInfo primaryKey, 
            Func<PropertyInfo, string> getColumnName, 
            IEnumerable<PropertyInfo> properties) where T : class
        {
            var parameters = new List<SqlParameter>();
            var setClause = new StringBuilder();
            
            var updateProperties = properties.Where(p => p != primaryKey);

            foreach (var property in updateProperties)
            {
                var newValue = property.GetValue(entity);
                var oldValue = property.GetValue(existing);
                if (!Equals(newValue, oldValue))
                {
                    var columnName = getColumnName(property);
                    setClause.Append($"{columnName} = @{property.Name}, ");
                    parameters.Add(CreateParameter($"@{property.Name}", newValue, property.PropertyType));
                }
            }

            if (setClause.Length == 0)
            {
                return (string.Empty, parameters);
            }

            // Remove trailing comma and space
            setClause.Length -= 2;

            var pkColumn = getColumnName(primaryKey);
            var pkValue = primaryKey.GetValue(entity);
            parameters.Add(CreateParameter("@Id", pkValue, primaryKey.PropertyType));

            var commandText = $"UPDATE {tableName} SET {setClause} WHERE {pkColumn} = @Id";
            return (commandText, parameters);
        }

        public string BuildDeleteCommand(string tableName, string whereClause)
        {
            return $"DELETE FROM {tableName}{whereClause}";
        }

        public string BuildSelectCommand(string tableName, string columnNames, string whereClause, string joinClauses = null)
        {
            var sql = $"SELECT {columnNames} FROM {tableName}";
            if (!string.IsNullOrEmpty(joinClauses))
            {
                sql += joinClauses;
            }
            if (!string.IsNullOrEmpty(whereClause))
            {
                sql += whereClause;
            }
            return sql;
        }

        private SqlParameter CreateParameter(string name, object value, Type type)
        {
            var param = new SqlParameter(name, value ?? DBNull.Value);
            if (value == null)
            {
                param.IsNullable = true;
            }
            return param;
        }
    }
}
