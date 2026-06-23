using System;

namespace Funcular.Data.Orm.Attributes
{
    /// <summary>
    /// Specifies the stored procedure name for an entity class used with
    /// <c>ExecProcedure&lt;T&gt;</c>. Analogous to <see cref="System.ComponentModel.DataAnnotations.Schema.TableAttribute"/>
    /// for table-name overrides. When absent, the procedure name is inferred from the class name by convention.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class ProcedureAttribute : Attribute
    {
        /// <summary>The stored procedure name.</summary>
        public string Name { get; }

        /// <summary>Initializes the attribute with the stored procedure name.</summary>
        public ProcedureAttribute(string name) => Name = name;
    }
}
