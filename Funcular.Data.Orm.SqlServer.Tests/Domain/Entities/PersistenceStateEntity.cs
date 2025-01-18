using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities
{
    /// <summary>
    /// Implements INotifyPropertyChanged. Provides a protected <see cref="SetProperty{T}"/>
    /// method for derived classes to call when setting properties. This checks the incoming
    /// value for equality with the property backing field, raising the PropertyChanged event
    /// only if value is different.
    /// </summary>
    public class PersistenceStateEntity : INotifyPropertyChanged
    {

        private PropertyChangedEventHandler? _propertyChanged;

        /// <summary>
        /// For the caller’s use.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add
            {
                if (_propertyChanged != null) _propertyChanged -= value;
                _propertyChanged += value;
            }
            remove
            {
                if (_propertyChanged != null) _propertyChanged -= value;
            }
        }


        /// <summary>
        /// The <paramref name="propertyName"/> parameter has the [CallerMemberName] attribute, so it's 
        /// best to omit this value when calling.
        /// </summary>
        /// <param name="propertyName"></param>
        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        ///     Checks if a property already matches a desired value.  Sets the property and
        ///     notifies listeners only when necessary.
        /// </summary>
        /// <typeparam name="T">Type of the property.</typeparam>
        /// <param name="storage">Reference to a property with both getter and setter.</param>
        /// <param name="value">Desired value for the property.</param>
        /// <param name="propertyName">
        ///     Name of the property used to notify listeners.  This
        ///     value is optional and can be provided automatically when invoked from compilers that
        ///     support CallerMemberName.
        /// </param>
        /// <returns>
        ///     True if the value was changed, false if the existing value matched the
        ///     desired value.
        /// </returns>
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (propertyName == null)
                return false;

            if (Equals(storage, value))
            {
                return false;
            }

            // this setter sets the backing field, not the property, so that it doesn't
            // recursively trigger this very method:
            storage = value;
            this.OnPropertyChanged(propertyName);
            return true;
        }
    }
}
