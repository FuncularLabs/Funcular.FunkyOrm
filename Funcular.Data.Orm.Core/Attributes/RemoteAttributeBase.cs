using System;

namespace Funcular.Data.Orm.Attributes
{
    /// <summary>
    /// Base class for remote mapping attributes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public abstract class RemoteAttributeBase : Attribute
    {
        /// <summary>
        /// Gets the type of the remote entity.
        /// </summary>
        public Type RemoteEntityType { get; }

        /// <summary>
        /// Gets the path to the remote key or property.
        /// </summary>
        public string[] KeyPath { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteAttributeBase"/> class.
        /// </summary>
        /// <param name="remoteEntityType">The type of the remote entity.</param>
        /// <param name="keyPath">The path to the remote key or property.</param>
        protected RemoteAttributeBase(Type remoteEntityType, params string[] keyPath)
        {
            RemoteEntityType = remoteEntityType;
            KeyPath = keyPath;
        }
    }
}
