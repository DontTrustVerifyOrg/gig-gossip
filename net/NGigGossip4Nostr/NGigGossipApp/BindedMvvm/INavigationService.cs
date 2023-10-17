using BindedMvvm.Abstractions;

namespace BindedMvvm
{
	public interface INavigationService
    {
        BindedViewModel CurrentViewModel { get; }

        Task NavigateAsync<TViewModel>(Action<object> onClosed = null, bool animated = false) where TViewModel : BindedViewModel;
        Task NavigateAsync<TViewModel, TPreparingData>(TPreparingData preparingData, Action<object> onClosed = null, bool animated = false) where TViewModel : BindedViewModel<TPreparingData>;
        Task NavigateBackAsync(object returnObject = null, int skipMore = 0, bool animated = false);
        Task NavigateBackToAsync<TViewModel>(object returnObject = null, bool animated = false) where TViewModel : BindedViewModel;
        Task NavigateBackBeforeAsync<TViewModel>(object returnObject = null, bool animated = false) where TViewModel : BindedViewModel;
    }
}

