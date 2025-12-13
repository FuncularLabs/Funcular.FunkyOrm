using System;

namespace Funcular.Data.Orm.Attributes
{
    /// <summary>
    /// Decorates a property to indicate it maps to a Primary Key in a related table.
    /// The ORM will automatically generate the necessary LEFT JOINs to retrieve the value.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class RemoteKeyAttribute : RemoteAttributeBase
    {
        /// <summary>
        /// Defines a remote key mapping.
        /// </summary>
        /// <param name="remoteEntityType">The type of the remote entity.</param>
        /// <param name="keyPath">
        /// If 1 argument: The name of the target property on TRemoteEntity. (Inference Mode)
        /// If >1 arguments: The ordered chain of Foreign Key properties, ending with the target property. (Explicit Mode)
        /// </param>
        public RemoteKeyAttribute(Type remoteEntityType, params string[] keyPath) : base(remoteEntityType, keyPath)
        {
        }
    }
}
