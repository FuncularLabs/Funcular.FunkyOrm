using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.Interfaces;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite
{
    /// <summary>
    /// SQLite implementation of the <see cref="ISqlDialect"/>.
    /// </summary>
    public class SqliteDialect : ISqlDialect
    {
        /// <inheritdoc />
        public string ProviderName => "sqlite";

        private static readonly HashSet<string> _reservedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ABORT", "ACTION", "ADD", "AFTER", "ALL", "ALTER", "ANALYZE", "AND", "AS", "ASC", "ATTACH",
            "AUTOINCREMENT", "BEFORE", "BEGIN", "BETWEEN", "BY", "CASCADE", "CASE", "CAST", "CHECK",
            "COLLATE", "COLUMN", "COMMIT", "CONFLICT", "CONSTRAINT", "CREATE", "CROSS", "CURRENT",
            "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP", "DATABASE", "DEFAULT", "DEFERRABLE",
            "DEFERRED", "DELETE", "DESC", "DETACH", "DISTINCT", "DO", "DROP", "EACH", "ELSE", "END",
            "ESCAPE", "EXCEPT", "EXCLUSIVE", "EXISTS", "EXPLAIN", "FAIL", "FILTER", "FOLLOWING", "FOR",
            "FOREIGN", "FROM", "FULL", "GLOB", "GROUP", "HAVING", "IF", "IGNORE", "IMMEDIATE", "IN",
            "INDEX", "INDEXED", "INITIALLY", "INNER", "INSERT", "INSTEAD", "INTERSECT", "INTO", "IS",
            "ISNULL", "JOIN", "KEY", "LEFT", "LIKE", "LIMIT", "MATCH", "NATURAL", "NO", "NOT", "NOTHING",
            "NOTNULL", "NULL", "OF", "OFFSET", "ON", "OR", "ORDER", "OUTER", "OVER", "PARTITION", "PLAN",
            "PRAGMA", "PRECEDING", "PRIMARY", "QUERY", "RAISE", "RANGE", "RECURSIVE", "REFERENCES",
            "REGEXP", "REINDEX", "RELEASE", "RENAME", "REPLACE", "RESTRICT", "RIGHT", "ROLLBACK", "ROW",
            "ROWS", "SAVEPOINT", "SELECT", "SET", "TABLE", "TEMP", "TEMPORARY", "THEN", "TO", "TRANSACTION",
            "TRIGGER", "UNBOUNDED", "UNION", "UNIQUE", "UPDATE", "USING", "VACUUM", "VALUES", "VIEW",
            "VIRTUAL", "WHEN", "WHERE", "WINDOW", "WITH", "WITHOUT"
        };

        public string EncloseIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return identifier;
            if (identifier.StartsWith("\"") && identifier.EndsWith("\"")) return identifier;
            if (identifier.StartsWith("[") && identifier.EndsWith("]"))
                identifier = identifier.Substring(1, identifier.Length - 2);
            return IsReservedWord(identifier) ? $"\"{identifier}\"" : identifier;
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

            var parameters = new List<SqliteParameter>();
            var columnNames = new List<string>();
            var parameterNames = new List<string>();

            foreach (var property in includedProperties)
            {
                var columnName = EncloseIdentifier(getColumnName(property));
                var value = property.GetValue(entity);
                var parameterName = $"@{property.Name}";

                columnNames.Add(columnName);
                parameterNames.Add(parameterName);
                parameters.Add(CreateParameter(parameterName, value, property.PropertyType));
            }

            // SQLite uses last_insert_rowid() for identity columns; for non-identity PKs use RETURNING
            string commandText;
            if (includePrimaryKey)
            {
                commandText = $@"INSERT INTO {tableName} ({string.Join(", ", columnNames)})
                VALUES ({string.Join(", ", parameterNames)});
                SELECT {EncloseIdentifier(getColumnName(primaryKey))} FROM {tableName} WHERE rowid = last_insert_rowid()";
            }
            else
            {
                commandText = $@"INSERT INTO {tableName} ({string.Join(", ", columnNames)})
                VALUES ({string.Join(", ", parameterNames)});
                SELECT last_insert_rowid()";
            }

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
            var parameters = new List<SqliteParameter>();
            var setClause = new StringBuilder();

            var updateProperties = properties.Where(p => p != primaryKey);

            foreach (var property in updateProperties)
            {
                var newValue = property.GetValue(entity);
                var oldValue = property.GetValue(existing);
                if (!Equals(newValue, oldValue))
                {
                    var columnName = EncloseIdentifier(getColumnName(property));
                    setClause.Append($"{columnName} = @{property.Name}, ");
                    parameters.Add(CreateParameter($"@{property.Name}", newValue, property.PropertyType));
                }
            }

            if (setClause.Length == 0)
            {
                return (string.Empty, parameters);
            }

            setClause.Length -= 2;

            var pkColumn = EncloseIdentifier(getColumnName(primaryKey));
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
                sql += joinClauses;
            if (!string.IsNullOrEmpty(whereClause))
                sql += whereClause;
            return sql;
        }

        /// <inheritdoc />
        public string BuildJsonValueExpression(string qualifiedColumn, string jsonPath, string castType = null)
        {
            // SQLite uses json_extract(column, '$.path')
            var path = jsonPath;
            if (!path.StartsWith("$"))
                path = "$." + path;

            var expr = $"json_extract({qualifiedColumn}, '{path}')";
            if (!string.IsNullOrEmpty(castType))
            {
                expr = $"CAST({expr} AS {castType})";
            }
            return expr;
        }

        /// <inheritdoc />
        public string BuildScalarSubquery(string childTableName, string childFkColumn, string parentPkExpression,
            AggregateFunction function, string aggregateColumn = null,
            string conditionColumn = null, string conditionValue = null)
        {
            string aggExpr;
            switch (function)
            {
                case AggregateFunction.Count:
                case AggregateFunction.ConditionalCount:
                    aggExpr = "COUNT(*)";
                    break;
                case AggregateFunction.Sum:
                    aggExpr = $"SUM({aggregateColumn})";
                    break;
                case AggregateFunction.Avg:
                    aggExpr = $"AVG({aggregateColumn})";
                    break;
                default:
                    aggExpr = "COUNT(*)";
                    break;
            }

            var sb = new StringBuilder();
            sb.Append($"(SELECT {aggExpr} FROM {childTableName} WHERE {childFkColumn} = {parentPkExpression}");
            if (!string.IsNullOrEmpty(conditionColumn) && conditionValue != null)
            {
                sb.Append($" AND {conditionColumn} = '{conditionValue}'");
            }
            sb.Append(")");
            return sb.ToString();
        }

        /// <inheritdoc />
        public string BuildJsonCollectionSubquery(string childTableName, string childFkColumn, string parentPkExpression,
            IList<string> columnExpressions, string orderByColumn = null)
        {
            // SQLite uses json_group_array(json_object(...)) for JSON collection subqueries
            var columns = columnExpressions.Count == 1 && columnExpressions[0] == "*"
                ? "*"
                : string.Join(", ", columnExpressions.Select(c => $"'{c}', {c}"));

            var jsonObj = columnExpressions.Count == 1 && columnExpressions[0] == "*"
                ? "json_object(*)"
                : $"json_object({columns})";

            var sb = new StringBuilder();
            sb.Append($"(SELECT json_group_array({jsonObj}) FROM {childTableName} WHERE {childFkColumn} = {parentPkExpression}");
            if (!string.IsNullOrEmpty(orderByColumn))
                sb.Append($" ORDER BY {orderByColumn}");
            sb.Append(")");
            return sb.ToString();
        }

        internal static SqliteParameter CreateParameter(string name, object value, Type propertyType)
        {
            if (value == null)
                return new SqliteParameter(name, DBNull.Value);

            // SQLite type affinity: convert special types
            if (value is DateTime dt)
                return new SqliteParameter(name, dt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            if (value is DateTimeOffset dto)
                return new SqliteParameter(name, dto.ToString("yyyy-MM-dd HH:mm:ss.fffffffK"));
            if (value is Guid guid)
                return new SqliteParameter(name, guid.ToString());
            if (value is bool b)
                return new SqliteParameter(name, b ? 1 : 0);
            if (value is Enum e)
                return new SqliteParameter(name, Convert.ToInt32(e));

            return new SqliteParameter(name, value);
        }
    }
}
