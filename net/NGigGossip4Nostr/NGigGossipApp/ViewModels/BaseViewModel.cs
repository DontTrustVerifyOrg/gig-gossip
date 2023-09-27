using BindedMvvm;
using BindedMvvm.Abstractions;

namespace GigMobile.ViewModels
{
    public interface IBaseViewModel
    {
        public INavigationService NavigationService { get; }
    }

    public abstract class BaseViewModel : BindedViewModel, IBaseViewModel
    {
		public INavigationService NavigationService { get; private set; }

		public BaseViewModel()
		{
            NavigationService = Application.Current.Handler.MauiContext.Services.GetService<INavigationService>();
        }
    }

    public abstract class BaseViewModel<T> : BindedViewModel<T>, IBaseViewModel
    {
        public INavigationService NavigationService { get; private set; }

        public BaseViewModel()
        {
            NavigationService = Application.Current.Handler.MauiContext.Services.GetService<INavigationService>();
        }
    }
}

