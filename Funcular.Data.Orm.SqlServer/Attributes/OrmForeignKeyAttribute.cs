using System;

namespace Funcular.Data.Orm.Attributes
{
    /// <summary>
    /// Explicitly specifies the target entity type for a foreign key property.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class OrmForeignKeyAttribute : Attribute
    {
        public Type TargetType { get; }

        public OrmForeignKeyAttribute(Type targetType)
        {
            TargetType = targetType;
        }
    }
}
