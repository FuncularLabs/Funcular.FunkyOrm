using System.Data;

namespace Funcular.Data.Orm
{
    /// <summary>
    /// Represents a named parameter for stored procedure execution. Supports input, output, and
    /// input/output directions, and converts implicitly from <c>(string Name, object Value)</c> tuples
    /// so terse call sites work: <c>ExecScalar&lt;int&gt;("proc", ("@gender", "Male"))</c>.
    /// </summary>
    public class SqlParam
    {
        /// <summary>The parameter name. A leading <c>@</c> is optional; providers apply their own prefix convention.</summary>
        public string Name { get; set; }

        /// <summary>The parameter value. <c>null</c> is sent as <see cref="System.DBNull"/>.</summary>
        public object? Value { get; set; }

        /// <summary>The parameter direction. Defaults to <see cref="ParameterDirection.Input"/>.</summary>
        public ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        /// <summary>
        /// Optional explicit database type. Required for <see cref="ParameterDirection.Output"/> parameters
        /// whose <see cref="Value"/> is <c>null</c>, so the native parameter can be typed.
        /// </summary>
        public DbType? DbType { get; set; }

        /// <summary>Optional size for variable-length types (typically paired with an output <see cref="DbType"/>).</summary>
        public int? Size { get; set; }

        /// <summary>Initializes an input parameter.</summary>
        public SqlParam(string name, object? value)
        {
            Name = name;
            Value = value;
        }

        /// <summary>Initializes a parameter with an explicit direction.</summary>
        public SqlParam(string name, object? value, ParameterDirection direction)
            : this(name, value)
        {
            Direction = direction;
        }

        /// <summary>Preserves terse tuple syntax: <c>ExecScalar&lt;int&gt;("proc", ("@gender", "Male"))</c>.</summary>
        public static implicit operator SqlParam((string Name, object? Value) tuple)
            => new SqlParam(tuple.Name, tuple.Value);
    }
}
