using System;

namespace Funcular.Data.Orm.Attributes
{
    /// <summary>
    /// Explicitly specifies the target entity type for a foreign key property.
    /// Use this attribute when the property name does not follow the standard naming convention (e.g., [EntityName]Id)
    /// to help the ORM infer the relationship.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class RemoteLinkAttribute : Attribute
    {
        /// <summary>
        /// Gets the type of the target entity that this foreign key points to.
        /// </summary>
        public Type TargetType { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteLinkAttribute"/> class.
        /// </summary>
        /// <param name="targetType">The type of the target entity that this foreign key points to.</param>
        public RemoteLinkAttribute(Type targetType)
        {
            TargetType = targetType;
        }
    }
}
