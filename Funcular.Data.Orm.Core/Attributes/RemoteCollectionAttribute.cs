using System;

namespace Funcular.Data.Orm.Attributes
{
    /// <summary>
    /// Specifies that a collection property should be populated with entities from a related table.
    /// The relationship path is resolved automatically or explicitly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class RemoteCollectionAttribute : RemoteAttributeBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteCollectionAttribute"/> class.
        /// </summary>
        /// <param name="remoteEntityType">The type of the entities in the collection.</param>
        /// <param name="keyPath">The optional explicit path of foreign keys to traverse.</param>
        public RemoteCollectionAttribute(Type remoteEntityType, params string[] keyPath) 
            : base(remoteEntityType, keyPath)
        {
        }
    }
}
