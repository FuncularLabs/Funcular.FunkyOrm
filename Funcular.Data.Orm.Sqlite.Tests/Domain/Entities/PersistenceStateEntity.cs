using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Funcular.Data.Orm.Sqlite.Tests.Domain.Entities
{
    public class PersistenceStateEntity : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler _propertyChanged;

        public event PropertyChangedEventHandler PropertyChanged
        {
            add { if (_propertyChanged != null) _propertyChanged -= value; _propertyChanged += value; }
            remove { if (_propertyChanged != null) _propertyChanged -= value; }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
