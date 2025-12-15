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
        /// Initializes a new instance of the <see cref="RemoteKeyAttribute"/> class.
        /// </summary>
        /// <param name="remoteEntityType">The type of the remote entity containing the key.</param>
        /// <param name="keyPath">
        /// The path to the remote key.
        /// <para>
        /// If 1 argument is provided, it is treated as the name of the target property on <paramref name="remoteEntityType"/> (Inference Mode).
        /// </para>
        /// <para>
        /// If multiple arguments are provided, they represent the ordered chain of Foreign Key properties to traverse, ending with the target property (Explicit Mode).
        /// </para>
        /// </param>
        public RemoteKeyAttribute(Type remoteEntityType, params string[] keyPath) : base(remoteEntityType, keyPath)
        {
        }
    }
}
