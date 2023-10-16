using System;
using System.ComponentModel;

namespace BindedMvvm.Abstractions
{
    public abstract class BindedViewModel : INotifyPropertyChanged
    {
        #region Fody
#pragma warning disable CS8612 // Fody implemenation.
#pragma warning disable CS8618 // Fody implemenation.
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS8618 // Fody implemenation.
#pragma warning restore CS8612 // Fody implemenation.

        #endregion

        internal Action<object?>? OnClosed;

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