using System;
using System.ComponentModel;

namespace BindedMvvm.Abstractions
{
    public abstract class BindedViewModel : INotifyPropertyChanged
    {
        #region Fody
        public event PropertyChangedEventHandler PropertyChanged;
        #endregion

        internal Action<object> OnClosed;

        public virtual Task Initialize()
        {
            return Task.CompletedTask;
        }

        public virtual void OnAppearing()
        {
        }

        public virtual void OnDisappearing()
        {
        }
    }

    public abstract class BindedViewModel<TPreparingData> : BindedViewModel
    {
        public abstract void Prepare(TPreparingData data);
    }
}