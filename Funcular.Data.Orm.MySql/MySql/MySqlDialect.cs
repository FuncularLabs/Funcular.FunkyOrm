using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.Interfaces;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql
{
    /// <summary>
    /// MySQL implementation of the <see cref="ISqlDialect"/>.
    /// Uses backtick identifier quoting, <c>LAST_INSERT_ID()</c> for identity retrieval
    /// (no <c>RETURNING</c> clause — the provider reads <see cref="MySqlCommand.LastInsertedId"/>
    /// after an identity INSERT), and native MySQL JSON functions.
    /// </summary>
    public class MySqlDialect : ISqlDialect
    {
        /// <inheritdoc />
        public string ProviderName => "mysql";

        // MySQL 8.0 reserved keywords that commonly collide with entity/column names.
        // Source: https://dev.mysql.com/doc/refman/8.0/en/keywords.html (reserved subset).
        private static readonly HashSet<string> _reservedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ACCESSIBLE", "ADD", "ALL", "ALTER", "ANALYZE", "AND", "AS", "ASC", "ASENSITIVE", "BEFORE",
            "BETWEEN", "BIGINT", "BINARY", "BLOB", "BOTH", "BY", "CALL", "CASCADE", "CASE", "CHANGE",
            "CHAR", "CHARACTER", "CHECK", "COLLATE", "COLUMN", "CONDITION", "CONSTRAINT", "CONTINUE",
            "CONVERT", "CREATE", "CROSS", "CUME_DIST", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
            "CURRENT_USER", "CURSOR", "DATABASE", "DATABASES", "DAY_HOUR", "DAY_MICROSECOND", "DAY_MINUTE",
            "DAY_SECOND", "DEC", "DECIMAL", "DECLARE", "DEFAULT", "DELAYED", "DELETE", "DENSE_RANK", "DESC",
            "DESCRIBE", "DETERMINISTIC", "DISTINCT", "DISTINCTROW", "DIV", "DOUBLE", "DROP", "DUAL", "EACH",
            "ELSE", "ELSEIF", "EMPTY", "ENCLOSED", "ESCAPED", "EXCEPT", "EXISTS", "EXIT", "EXPLAIN", "FALSE",
            "FETCH", "FIRST_VALUE", "FLOAT", "FOR", "FORCE", "FOREIGN", "FROM", "FULLTEXT", "FUNCTION",
            "GENERATED", "GET", "GRANT", "GROUP", "GROUPING", "GROUPS", "HAVING", "HIGH_PRIORITY",
            "HOUR_MICROSECOND", "HOUR_MINUTE", "HOUR_SECOND", "IF", "IGNORE", "IN", "INDEX", "INFILE",
            "INNER", "INOUT", "INSENSITIVE", "INSERT", "INT", "INTEGER", "INTERVAL", "INTO", "IO_AFTER_GTIDS",
            "IS", "ITERATE", "JOIN", "JSON_TABLE", "KEY", "KEYS", "KILL", "LAG", "LAST_VALUE", "LATERAL",
            "LEAD", "LEADING", "LEAVE", "LEFT", "LIKE", "LIMIT", "LINEAR", "LINES", "LOAD", "LOCALTIME",
            "LOCALTIMESTAMP", "LOCK", "LONG", "LONGBLOB", "LONGTEXT", "LOOP", "LOW_PRIORITY", "MASTER_BIND",
            "MATCH", "MAXVALUE", "MEDIUMBLOB", "MEDIUMINT", "MEDIUMTEXT", "MIDDLEINT", "MINUTE_MICROSECOND",
            "MINUTE_SECOND", "MOD", "MODIFIES", "NATURAL", "NOT", "NO_WRITE_TO_BINLOG", "NTH_VALUE", "NTILE",
            "NULL", "NUMERIC", "OF", "ON", "OPTIMIZE", "OPTION", "OPTIONALLY", "OR", "ORDER", "OUT", "OUTER",
            "OUTFILE", "OVER", "PARTITION", "PERCENT_RANK", "PRECISION", "PRIMARY", "PROCEDURE", "PURGE",
            "RANGE", "RANK", "READ", "READS", "READ_WRITE", "REAL", "RECURSIVE", "REFERENCES", "REGEXP",
            "RELEASE", "RENAME", "REPEAT", "REPLACE", "REQUIRE", "RESIGNAL", "RESTRICT", "RETURN", "REVOKE",
            "RIGHT", "RLIKE", "ROW", "ROWS", "ROW_NUMBER", "SCHEMA", "SCHEMAS", "SECOND_MICROSECOND",
            "SELECT", "SENSITIVE", "SEPARATOR", "SET", "SHOW", "SIGNAL", "SMALLINT", "SPATIAL", "SPECIFIC",
            "SQL", "SQLEXCEPTION", "SQLSTATE", "SQLWARNING", "SQL_BIG_RESULT", "SQL_CALC_FOUND_ROWS",
            "SQL_SMALL_RESULT", "SSL", "STARTING", "STORED", "STRAIGHT_JOIN", "SYSTEM", "TABLE", "TERMINATED",
            "THEN", "TINYBLOB", "TINYINT", "TINYTEXT", "TO", "TRAILING", "TRIGGER", "TRUE", "UNDO", "UNION",
            "UNIQUE", "UNLOCK", "UNSIGNED", "UPDATE", "USAGE", "USE", "USING", "UTC_DATE", "UTC_TIME",
            "UTC_TIMESTAMP", "VALUES", "VARBINARY", "VARCHAR", "VARCHARACTER", "VARYING", "VIRTUAL", "WHEN",
            "WHERE", "WHILE", "WINDOW", "WITH", "WRITE", "XOR", "YEAR_MONTH", "ZEROFILL"
        };

        public string EncloseIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return identifier;
            // Already backtick-quoted
            if (identifier.StartsWith("`") && identifier.EndsWith("`")) return identifier;
            // Strip SQL Server bracket quoting if present
            if (identifier.StartsWith("[") && identifier.EndsWith("]"))
                identifier = identifier.Substring(1, identifier.Length - 2);
            // Strip ANSI double-quote if present
            if (identifier.StartsWith("\"") && identifier.EndsWith("\""))
                identifier = identifier.Substring(1, identifier.Length - 2);
            return IsReservedWord(identifier) ? $"`{identifier}`" : identifier;
        }

        public bool IsReservedWord(string word)
        {
            return word != null && _reservedWords.Contains(word.ToUpperInvariant());
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

            var parameters = new List<MySqlParameter>();
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

            // MySQL has no RETURNING clause. Emit a plain INSERT; the provider retrieves the
            // generated identity via MySqlCommand.LastInsertedId after ExecuteNonQuery.
            var commandText = $@"
                INSERT INTO {tableName} ({string.Join(", ", columnNames)})
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
            var parameters = new List<MySqlParameter>();
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
            // MySQL: JSON_UNQUOTE(JSON_EXTRACT(col, '$.path')) extracts a scalar as text.
            // The jsonPath already uses SQL Server-style '$.path' syntax, which MySQL accepts directly.
            var path = jsonPath;
            if (!path.StartsWith("$"))
                path = "$." + path.TrimStart('.');

            var expr = $"JSON_UNQUOTE(JSON_EXTRACT({qualifiedColumn}, '{path}'))";
            if (!string.IsNullOrEmpty(castType))
            {
                expr = $"CAST({expr} AS {MapCastType(castType)})";
            }
            return expr;
        }

        /// <summary>
        /// Maps a generic/SQL Server cast type to its MySQL CAST target type.
        /// MySQL's CAST accepts a limited set of types (e.g., SIGNED, UNSIGNED, DECIMAL, CHAR, DATE, DATETIME).
        /// </summary>
        private static string MapCastType(string castType)
        {
            switch (castType.Trim().ToLowerInvariant())
            {
                case "int":
                case "integer":
                case "bigint":
                case "smallint":
                case "tinyint":
                case "long":
                    return "SIGNED";
                case "bit":
                case "bool":
                case "boolean":
                    return "UNSIGNED";
                case "float":
                case "real":
                case "double":
                case "decimal":
                case "money":
                case "numeric":
                    return "DECIMAL(38,10)";
                case "date":
                    return "DATE";
                case "datetime":
                case "datetime2":
                case "timestamp":
                    return "DATETIME";
                default:
                    // nvarchar/varchar/text/char and anything unknown → CHAR
                    return "CHAR";
            }
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
            // MySQL: JSON_ARRAYAGG(JSON_OBJECT('col', col, ...)) projects child rows as a JSON array.
            // Build key/value pairs for JSON_OBJECT from each column expression. The column expression
            // is already a resolved column name; use its unqualified tail as the JSON key.
            var pairs = new List<string>();
            foreach (var col in columnExpressions)
            {
                var key = col;
                var dot = key.LastIndexOf('.');
                if (dot >= 0) key = key.Substring(dot + 1);
                key = key.Trim('`', '"', '[', ']');
                pairs.Add($"'{key}', {col}");
            }

            var jsonObject = $"JSON_OBJECT({string.Join(", ", pairs)})";
            var sb = new StringBuilder();
            sb.Append($"(SELECT JSON_ARRAYAGG({jsonObject}) FROM (SELECT * FROM {childTableName}");
            sb.Append($" WHERE {childFkColumn} = {parentPkExpression}");
            if (!string.IsNullOrEmpty(orderByColumn))
            {
                sb.Append($" ORDER BY {orderByColumn}");
            }
            sb.Append(") sub)");
            return sb.ToString();
        }

        private MySqlParameter CreateParameter(string name, object value, Type type)
        {
            var param = new MySqlParameter(name, value ?? DBNull.Value);
            if (value == null)
            {
                param.IsNullable = true;
            }
            return param;
        }
    }
}
