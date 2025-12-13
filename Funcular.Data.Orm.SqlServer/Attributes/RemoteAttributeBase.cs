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
        /// The type of the remote entity.
        /// </summary>
        public Type RemoteEntityType { get; }

        /// <summary>
        /// The path to the remote key.
        /// </summary>
        public string[] KeyPath { get; }

        protected RemoteAttributeBase(Type remoteEntityType, params string[] keyPath)
        {
            RemoteEntityType = remoteEntityType;
            KeyPath = keyPath;
        }
    }
}
