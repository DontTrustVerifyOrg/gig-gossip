using System.Windows.Input;
using BindedMvvm;
using BindedMvvm.Abstractions;

namespace GigMobile.ViewModels
{
    public interface IBaseViewModel
    {
        public INavigationService NavigationService { get; }
        public bool IsBusy { get; }
        public ICommand BackCommand { get; }
    }

    public abstract class BaseViewModel : BindedViewModel, IBaseViewModel
    {
		public INavigationService NavigationService { get; private set; }

        public bool IsBusy { get; protected set; }

        public BaseViewModel()
		{
            NavigationService = Application.Current.Handler.MauiContext.Services.GetService<INavigationService>();
        }

        private ICommand _backCommand;
        public ICommand BackCommand => _backCommand ??= new Command(async () => await NavigationService.NavigateBackAsync());
    }

    public abstract class BaseViewModel<T> : BindedViewModel<T>, IBaseViewModel
    {
        public INavigationService NavigationService { get; private set; }

        public BaseViewModel()
        {
            NavigationService = Application.Current.Handler.MauiContext.Services.GetService<INavigationService>();
        }

        public bool IsBusy { get; protected set; }

        private ICommand _backCommand;
        public ICommand BackCommand => _backCommand ??= new Command(async () => await NavigationService.NavigateBackAsync());
    }
}

