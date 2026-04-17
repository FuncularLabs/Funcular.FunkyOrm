using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.Interfaces;
using Npgsql;
using NpgsqlTypes;

namespace Funcular.Data.Orm.PostgreSql
{
    /// <summary>
    /// PostgreSQL implementation of the <see cref="ISqlDialect"/>.
    /// </summary>
    public class PostgreSqlDialect : ISqlDialect
    {
        /// <inheritdoc />
        public string ProviderName => "postgresql";
        private static readonly HashSet<string> _reservedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ALL", "ANALYSE", "ANALYZE", "AND", "ANY", "ARRAY", "AS", "ASC", "ASYMMETRIC", "AUTHORIZATION",
            "BETWEEN", "BINARY", "BOTH", "CASE", "CAST", "CHECK", "COLLATE", "COLLATION", "COLUMN", "CONCURRENTLY",
            "CONSTRAINT", "CREATE", "CROSS", "CURRENT_CATALOG", "CURRENT_DATE", "CURRENT_ROLE", "CURRENT_SCHEMA",
            "CURRENT_TIME", "CURRENT_TIMESTAMP", "CURRENT_USER", "DEFAULT", "DEFERRABLE", "DESC", "DISTINCT", "DO",
            "ELSE", "END", "EXCEPT", "EXISTS", "FALSE", "FETCH", "FOR", "FOREIGN", "FREEZE", "FROM", "FULL",
            "GRANT", "GROUP", "HAVING", "ILIKE", "IN", "INITIALLY", "INNER", "INTERSECT", "INTO", "IS", "ISNULL",
            "JOIN", "KEY", "LATERAL", "LEADING", "LEFT", "LIKE", "LIMIT", "LOCALTIME", "LOCALTIMESTAMP", "NATURAL",
            "NOT", "NOTNULL", "NULL", "OFFSET", "ON", "ONLY", "OR", "ORDER", "OUTER", "OVERLAPS", "PLACING",
            "PRIMARY", "REFERENCES", "RETURNING", "RIGHT", "SELECT", "SESSION_USER", "SIMILAR", "SOME", "SYMMETRIC",
            "TABLE", "TABLESAMPLE", "THEN", "TO", "TRAILING", "TRUE", "UNION", "UNIQUE", "USER", "USING", "VARIADIC",
            "VERBOSE", "WHEN", "WHERE", "WINDOW", "WITH"
        };

        public string EncloseIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return identifier;
            // Already quoted
            if (identifier.StartsWith("\"") && identifier.EndsWith("\"")) return identifier;
            // Also handle if it was bracketed (from SqlServer convention)
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

            var parameters = new List<NpgsqlParameter>();
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

            // Use RETURNING instead of OUTPUT INSERTED
            var commandText = $@"
                INSERT INTO {tableName} ({string.Join(", ", columnNames)})
                VALUES ({string.Join(", ", parameterNames)})
                RETURNING {EncloseIdentifier(getColumnName(primaryKey))}";

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
            var parameters = new List<NpgsqlParameter>();
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
            {
                sql += joinClauses;
            }
            if (!string.IsNullOrEmpty(whereClause))
            {
                sql += whereClause;
            }
            return sql;
        }

        /// <inheritdoc />
        public string BuildJsonValueExpression(string qualifiedColumn, string jsonPath, string castType = null)
        {
            // Convert JSON path from SQL Server syntax ($.client.name) to PostgreSQL array syntax ({client,name})
            // Strip leading "$." and split on "."
            var path = jsonPath;
            if (path.StartsWith("$."))
                path = path.Substring(2);
            else if (path.StartsWith("$"))
                path = path.Substring(1);

            var segments = path.Split('.');
            var pgPath = "{" + string.Join(",", segments) + "}";

            // PostgreSQL: column #>> '{path}' extracts as text
            var expr = $"{qualifiedColumn} #>> '{pgPath}'";
            if (!string.IsNullOrEmpty(castType))
            {
                expr = $"({expr})::{castType}";
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

            if (function == AggregateFunction.ConditionalCount && !string.IsNullOrEmpty(conditionColumn) && conditionValue != null)
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
            var columns = string.Join(", ", columnExpressions);
            var sb = new StringBuilder();
            sb.Append($"(SELECT json_agg(row_to_json(sub)) FROM (SELECT {columns} FROM {childTableName}");
            sb.Append($" WHERE {childFkColumn} = {parentPkExpression}");
            if (!string.IsNullOrEmpty(orderByColumn))
            {
                sb.Append($" ORDER BY {orderByColumn}");
            }
            sb.Append(") sub)");
            return sb.ToString();
        }

        private NpgsqlParameter CreateParameter(string name, object value, Type type)
        {
            var param = new NpgsqlParameter(name, value ?? DBNull.Value);
            if (value == null)
            {
                param.IsNullable = true;
            }
            return param;
        }
    }
}
