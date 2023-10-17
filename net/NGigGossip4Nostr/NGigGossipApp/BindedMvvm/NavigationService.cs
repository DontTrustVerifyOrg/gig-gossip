using BindedMvvm.Abstractions;
using BindedMvvm.Attributes;

namespace BindedMvvm
{
    public class NavigationService : INavigationService
    {
        private readonly Dictionary<Type, Type> _viewModelPageDictionary;
        private readonly IServiceProvider _serviceProvider;

        private static INavigation MauiNavigation => Application.Current.MainPage.Navigation;

        public BindedViewModel CurrentViewModel => MauiNavigation.NavigationStack.Count - 1 >= 0
            ? GetCurrentViewModel(MauiNavigation.NavigationStack[MauiNavigation.NavigationStack.Count - 1]) : null;

        public NavigationService(IServiceProvider serviceProvider)
        {
            _viewModelPageDictionary = GetMappedPopupViewToViewModel();
            _serviceProvider = serviceProvider;
        }

        public async Task NavigateAsync<TViewModel>(Action<object> onClosed, bool animated = false) where TViewModel : BindedViewModel
        {
            if (_viewModelPageDictionary.TryGetValue(typeof(TViewModel), out var pageType))
            {
                var potentialNoHistoryAttribute = Attribute.GetCustomAttribute(typeof(TViewModel), typeof(CleanHistoryAttribute));

                await Application.Current.Dispatcher.DispatchAsync(async () =>
                {
                    var page = await ResolvePageAsync<TViewModel>(onClosed, pageType);
                    await MauiNavigation.PushAsync(page, animated);
                    if (potentialNoHistoryAttribute != null && MauiNavigation.NavigationStack.Count > 1)
                        for (var i = MauiNavigation.NavigationStack.Count - 2; i >= 0; i--)
                            MauiNavigation.RemovePage(MauiNavigation.NavigationStack[i]);
                });
            }
            else
                throw new InvalidOperationException($"No Page with {typeof(TViewModel)} doesn't exists.");
        }

        public async Task NavigateAsync<TViewModel, TPreparingData>(TPreparingData preparingData, Action<object> onClosed, bool animated = false)
            where TViewModel : BindedViewModel<TPreparingData>
        {
            if (_viewModelPageDictionary.TryGetValue(typeof(TViewModel), out var pageType))
            {
                var potentialNoHistoryAttribute = Attribute.GetCustomAttribute(typeof(TViewModel), typeof(CleanHistoryAttribute));

                await Application.Current.Dispatcher.DispatchAsync(async () =>
                {
                    var page = await ResolvePageAsync<TViewModel, TPreparingData>(preparingData, onClosed, pageType);
                    await MauiNavigation.PushAsync(page, animated);
                    if (potentialNoHistoryAttribute != null && MauiNavigation.NavigationStack.Count > 1)
                        for (var i = MauiNavigation.NavigationStack.Count - 2; i >= 0; i--)
                            MauiNavigation.RemovePage(MauiNavigation.NavigationStack[i]);
                });
            }
            else
                throw new InvalidOperationException($"No Page with {typeof(TViewModel)} doesn't exists.");
        }

        public async Task NavigateBackAsync(object returnObject, int skipMore = 0, bool animated = false)
        {
            var navigationStackCount = MauiNavigation.NavigationStack.Count;
            if (navigationStackCount < (2 + skipMore))
                throw new InvalidOperationException("Unable to go back, NavigationStack contains no more pages.");

            var targetPage = MauiNavigation.NavigationStack[navigationStackCount - 2 - skipMore];

            while (MauiNavigation.NavigationStack[MauiNavigation.NavigationStack.Count - 2].GetType() != targetPage.GetType())
            {
                MauiNavigation.RemovePage(MauiNavigation.NavigationStack[MauiNavigation.NavigationStack.Count - 2]);
            }

            var targetViewModel = (MauiNavigation.NavigationStack[MauiNavigation.NavigationStack.Count - 1].BindingContext as BindedViewModel);
            await Application.Current.Dispatcher.DispatchAsync(async () => await MauiNavigation.PopAsync(animated));

            targetViewModel?.OnClosed?.Invoke(returnObject);
        }

        public async Task NavigateBackToAsync<TViewModel>(object returnObject, bool animated = false) where TViewModel : BindedViewModel
        {
            var targetPage = MauiNavigation.NavigationStack.FirstOrDefault(x => x.BindingContext?.GetType() == typeof(TViewModel))
                ?? throw new InvalidOperationException($"Unable to go {typeof(TViewModel)}, NavigationStack don't contains target page.");
            while (MauiNavigation.NavigationStack[MauiNavigation.NavigationStack.Count - 2].GetType() != targetPage.GetType())
            {
                MauiNavigation.RemovePage(MauiNavigation.NavigationStack[MauiNavigation.NavigationStack.Count - 2]);
            }

            var targetViewModel = (MauiNavigation.NavigationStack[MauiNavigation.NavigationStack.Count - 1].BindingContext as BindedViewModel);
            await Application.Current.Dispatcher.DispatchAsync(async () => await MauiNavigation.PopAsync(animated));

            targetViewModel?.OnClosed?.Invoke(returnObject);
        }

        public async Task NavigateBackBeforeAsync<TViewModel>(object returnObject, bool animated = false) where TViewModel : BindedViewModel
        {
            var targetPage = MauiNavigation.NavigationStack.TakeWhile(x => x.BindingContext?.GetType() == typeof(TViewModel)).Last()
                ?? throw new InvalidOperationException($"Unable to go before {typeof(TViewModel)}, NavigationStack don't contains target page.");
            while (MauiNavigation.NavigationStack[MauiNavigation.NavigationStack.Count - 2].GetType() != targetPage.GetType())
            {
                MauiNavigation.RemovePage(MauiNavigation.NavigationStack[MauiNavigation.NavigationStack.Count - 2]);
            }

            var targetViewModel = (MauiNavigation.NavigationStack[MauiNavigation.NavigationStack.Count - 1].BindingContext as BindedViewModel);
            await Application.Current.Dispatcher.DispatchAsync(async () => await MauiNavigation.PopAsync(animated));

            targetViewModel?.OnClosed?.Invoke(returnObject);
        }

        private async Task<BindedPage<TViewModel>> ResolvePageAsync<TViewModel>(Action<object> onClosed, Type pageType) where TViewModel : BindedViewModel
        {
            var (viewModel, page) = ResolveViewModelAndPage<TViewModel>(pageType);
            viewModel.OnClosed = onClosed;
            await viewModel.Initialize();

            return page;
        }

        private async Task<BindedPage<TViewModel>> ResolvePageAsync<TViewModel, TPreparingData>(TPreparingData preparingData, Action<object> onClosed, Type pageType)
            where TViewModel : BindedViewModel<TPreparingData>
        {
            var (viewModelBase, page) = ResolveViewModelAndPage<TViewModel>(pageType);
            if (viewModelBase is not BindedViewModel<TPreparingData> viewModel)
                throw new InvalidOperationException($"{typeof(TViewModel)} should be defined as {typeof(TViewModel)}<{typeof(TPreparingData)}>.");
            viewModel.OnClosed = onClosed;
            viewModel.Prepare(preparingData);
            await viewModel.Initialize();

            return page;
        }

        public (BindedViewModel ViewModel, BindedPage<TViewModel> Page) ResolveViewModelAndPage<TViewModel>(Type pageType) where TViewModel : BindedViewModel
        {
            var constructorArguments = new List<object>();
            var viewModelConstructor = typeof(TViewModel).GetConstructors().FirstOrDefault()
                ?? throw new InvalidOperationException($"Could not find constructor for {typeof(TViewModel)}.");
            foreach (var parameters in viewModelConstructor.GetParameters())
                constructorArguments.Add(_serviceProvider.GetService(parameters.ParameterType));

            TViewModel viewModel;
            if (constructorArguments.Any())
                viewModel = (TViewModel)Activator.CreateInstance(typeof(TViewModel), constructorArguments.ToArray());
            else
                viewModel = (TViewModel)Activator.CreateInstance(typeof(TViewModel));

            var page = (BindedPage<TViewModel>)Activator.CreateInstance(pageType);
            page.BindingContext = viewModel;

            return (viewModel, page);
        }

        private static Dictionary<Type, Type> GetMappedPopupViewToViewModel()
        {
            try
            {
                var typesAssemblies = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes()).ToList();
                var pages = typesAssemblies
                    .Where(x => IsChildOfGenericType(x, typeof(BindedPage<>)))
                    .ToList();

                return pages.ToDictionary(GetGenericTypeParameter);
            }
            catch (Exception)
            {
                throw;
            }
        }

        private static bool IsChildOfGenericType(Type target, Type genericType)
        {
            if (target.BaseType == null || !target.BaseType.IsGenericType)
                return false;

            while (target.BaseType != null && target.BaseType.IsGenericType)
            {
                if (target.BaseType.GetGenericTypeDefinition() == genericType)
                    return true;
                target = target.BaseType;
            }

            return false;
        }

        private static Type GetGenericTypeParameter(Type type)
        {
            while (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(BindedPage<>))
                type = type.BaseType;
            return type.GetGenericArguments()[0];
        }

        private static BindedViewModel GetCurrentViewModel(Page currentPage)
        {
            return currentPage?.BindingContext as BindedViewModel;
        }
    }
}

