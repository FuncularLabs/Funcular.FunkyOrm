using System;

namespace Funcular.Data.Orm.Attributes
{
    /// <summary>
    /// Decorates a property to indicate it is a projection of a value (non-key) from a related table.
    /// The ORM will automatically generate the necessary LEFT JOINs to retrieve the value.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class RemotePropertyAttribute : RemoteAttributeBase
    {
        /// <summary>
        /// Defines a remote property mapping.
        /// </summary>
        /// <param name="remoteEntityType">The type of the remote entity.</param>
        /// <param name="keyPath">
        /// If 1 argument: The name of the target property on TRemoteEntity. (Inference Mode)
        /// If >1 arguments: The ordered chain of Foreign Key properties, ending with the target property. (Explicit Mode)
        /// </param>
        public RemotePropertyAttribute(Type remoteEntityType, params string[] keyPath) : base(remoteEntityType, keyPath)
        {
        }
    }
}
