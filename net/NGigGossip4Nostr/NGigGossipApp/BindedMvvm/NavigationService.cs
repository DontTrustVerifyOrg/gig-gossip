using System;
using System.Reflection;
using BindedMvvm.Abstractions;
using BindedMvvm.Attributes;

namespace BindedMvvm
{
    public class NavigationService : INavigationService
    {
        private readonly Dictionary<Type, Type> _viewModelPageDictionary;
        private readonly IServiceProvider _serviceProvider;

        private static INavigation _navigation => Application.Current!.MainPage!.Navigation;
        private static Page _currentPage => GetCurrentPage(Application.Current!.MainPage!);

        public BindedViewModel? CurrentViewModel => _navigation.NavigationStack.Count - 1 >= 0 ? GetCurrentViewModel(_navigation.NavigationStack[_navigation.NavigationStack.Count - 1]) : null;

        public NavigationService(IServiceProvider serviceProvider)
        {
            _viewModelPageDictionary = NavigationService.GetMappedPopupViewToViewModel();
            _serviceProvider = serviceProvider;
        }

        public async Task NavigateAsync<TViewModel>(Action<object?>? onClosed, bool animated = false) where TViewModel : BindedViewModel
        {
            if (_viewModelPageDictionary.TryGetValue(typeof(TViewModel), out var pageType))
            {
                var potentialNoHistoryAttribute = Attribute.GetCustomAttribute(typeof(TViewModel), typeof(CleanHistoryAttribute));
                var potentialTabAttribute = Attribute.GetCustomAttribute(typeof(TViewModel), typeof(TabbedViewModelAttribute));
                if (potentialTabAttribute is TabbedViewModelAttribute tabAttribute)
                {
                    if (CurrentViewModel?.GetType() != typeof(TViewModel) &&
                        CurrentViewModel?.GetType() != tabAttribute.ParentViewModel)
                    {
                        await Application.Current!.Dispatcher.DispatchAsync(async () =>
                        {
                            var page = await ResolvePageAsync<TViewModel>(onClosed, pageType);
                            await _navigation.PushAsync(page, animated);
                            if (potentialNoHistoryAttribute != null && _navigation.NavigationStack.Count > 1)
                                for (var i = _navigation.NavigationStack.Count - 2; i >= 0; i--)
                                    _navigation.RemovePage(_navigation.NavigationStack[i]);
                        });
                    }

                    if (NavigationService._currentPage is ITabViewPage tabViewPage)
                    {
                        tabViewPage.SelectTabIndex(tabAttribute.TabIndex);
                    }
                }
                else
                {
                    await Application.Current!.Dispatcher.DispatchAsync(async () =>
                    {
                        var page = await ResolvePageAsync<TViewModel>(onClosed, pageType);
                        await _navigation.PushAsync(page, animated);
                        if (potentialNoHistoryAttribute != null && _navigation.NavigationStack.Count > 1)
                            for (var i = _navigation.NavigationStack.Count - 2; i >= 0; i--)
                                _navigation.RemovePage(_navigation.NavigationStack[i]);
                    });
                }
            }
            else
                throw new InvalidOperationException($"No Page with {typeof(TViewModel)} doesn't exists.");
        }

        public async Task NavigateAsync<TViewModel, TPreparingData>(TPreparingData preparingData, Action<object?>? onClosed, bool animated = false) where TViewModel : BindedViewModel<TPreparingData>
        {
            if (_viewModelPageDictionary.TryGetValue(typeof(TViewModel), out var pageType))
            {
                var potentialNoHistoryAttribute = Attribute.GetCustomAttribute(typeof(TViewModel), typeof(CleanHistoryAttribute));
                var attr = Attribute.GetCustomAttribute(typeof(TViewModel), typeof(TabbedViewModelAttribute));
                if (attr is TabbedViewModelAttribute tabAttribute)
                {
                    if (CurrentViewModel?.GetType() != typeof(TViewModel) &&
                        CurrentViewModel?.GetType() != tabAttribute.ParentViewModel)
                    {
                        await Application.Current!.Dispatcher.DispatchAsync(async () =>
                        {
                            var page = await ResolvePageAsync<TViewModel, TPreparingData>(preparingData, onClosed, pageType);
                            await _navigation.PushAsync(page, animated);
                            if (potentialNoHistoryAttribute != null && _navigation.NavigationStack.Count > 1)
                                for (var i = _navigation.NavigationStack.Count - 2; i >= 0; i--)
                                    _navigation.RemovePage(_navigation.NavigationStack[i]);
                        });
                    }

                    if (NavigationService._currentPage is ITabViewPage tabViewPage)
                    {
                        tabViewPage.SelectTabIndex(tabAttribute.TabIndex);
                    }
                }
                else
                {
                    await Application.Current!.Dispatcher.DispatchAsync(async () =>
                    {
                        var page = await ResolvePageAsync<TViewModel, TPreparingData>(preparingData, onClosed, pageType);
                        await _navigation.PushAsync(page, animated);
                        if (potentialNoHistoryAttribute != null && _navigation.NavigationStack.Count > 1)
                            for (var i = _navigation.NavigationStack.Count - 2; i >= 0; i--)
                                _navigation.RemovePage(_navigation.NavigationStack[i]);
                    });
                }
            }
            else
                throw new InvalidOperationException($"No Page with {typeof(TViewModel)} doesn't exists.");
        }

        public async Task NavigateBackAsync(object? returnObject, int skipMore = 0, bool animated = false)
        {
            var navigationStackCount = _navigation.NavigationStack.Count;
            if (navigationStackCount < (2 + skipMore))
                throw new InvalidOperationException("Unable to go back, NavigationStack contains no more pages.");

            var targetPage = _navigation.NavigationStack[navigationStackCount - 2 - skipMore];

            while (_navigation.NavigationStack[_navigation.NavigationStack.Count - 2].GetType() != targetPage.GetType())
            {
                _navigation.RemovePage(_navigation.NavigationStack[_navigation.NavigationStack.Count - 2]);
            }

            var targetViewModel = (_navigation.NavigationStack[_navigation.NavigationStack.Count - 1].BindingContext as BindedViewModel);
            await Application.Current!.Dispatcher.DispatchAsync(async () => await _navigation.PopAsync(animated));

            targetViewModel?.OnClosed?.Invoke(returnObject);
        }

        public async Task NavigateBackToAsync<TViewModel>(object? returnObject, bool animated = false) where TViewModel : BindedViewModel
        {
            var targetPage = _navigation.NavigationStack.FirstOrDefault(x => x.BindingContext?.GetType() == typeof(TViewModel));
            if (targetPage == null)
                throw new InvalidOperationException($"Unable to go {typeof(TViewModel)}, NavigationStack don't contains target page.");

            while (_navigation.NavigationStack[_navigation.NavigationStack.Count - 2].GetType() != targetPage.GetType())
            {
                _navigation.RemovePage(_navigation.NavigationStack[_navigation.NavigationStack.Count - 2]);
            }

            var targetViewModel = (_navigation.NavigationStack[_navigation.NavigationStack.Count - 1].BindingContext as BindedViewModel);
            await Application.Current!.Dispatcher.DispatchAsync(async () => await _navigation.PopAsync(animated));

            targetViewModel?.OnClosed?.Invoke(returnObject);
        }

        public async Task NavigateBackBeforeAsync<TViewModel>(object? returnObject, bool animated = false) where TViewModel : BindedViewModel
        {
            var targetPage = _navigation.NavigationStack.TakeWhile(x => x.BindingContext?.GetType() == typeof(TViewModel)).Last();
            if (targetPage == null)
                throw new InvalidOperationException($"Unable to go before {typeof(TViewModel)}, NavigationStack don't contains target page.");

            while (_navigation.NavigationStack[_navigation.NavigationStack.Count - 2].GetType() != targetPage.GetType())
            {
                _navigation.RemovePage(_navigation.NavigationStack[_navigation.NavigationStack.Count - 2]);
            }

            var targetViewModel = (_navigation.NavigationStack[_navigation.NavigationStack.Count - 1].BindingContext as BindedViewModel);
            await Application.Current!.Dispatcher.DispatchAsync(async () => await _navigation.PopAsync(animated));

            targetViewModel?.OnClosed?.Invoke(returnObject);
        }

        private async Task<BindedPage<TViewModel>> ResolvePageAsync<TViewModel>(Action<object?>? onClosed, Type pageType) where TViewModel : BindedViewModel
        {
            var (viewModel, page) = ResolveViewModelAndPage<TViewModel>(pageType);
            viewModel.OnClosed = onClosed;
            await viewModel.Initialize();

            return page;
        }

        private async Task<BindedPage<TViewModel>> ResolvePageAsync<TViewModel, TPreparingData>(TPreparingData preparingData, Action<object?>? onClosed, Type pageType) where TViewModel : BindedViewModel<TPreparingData>
        {
            var (viewModelBase, page) = ResolveViewModelAndPage<TViewModel>(pageType);
            var viewModel = viewModelBase as BindedViewModel<TPreparingData>;
            if (viewModel == null)
                throw new InvalidOperationException($"{typeof(TViewModel)} should be defined as {typeof(TViewModel)}<{typeof(TPreparingData)}>.");
            viewModel!.OnClosed = onClosed;
            viewModel.Prepare(preparingData);
            await viewModel.Initialize();

            return page;
        }

        public (BindedViewModel ViewModel, BindedPage<TViewModel> Page) ResolveViewModelAndPage<TViewModel>(Type pageType) where TViewModel : BindedViewModel
        {
            var constructorArguments = new List<object>();
            var viewModelConstructor = typeof(TViewModel).GetConstructors().FirstOrDefault();

            if (viewModelConstructor == null)
                throw new InvalidOperationException($"Could not find constructor for {typeof(TViewModel)}.");

            foreach (var parameters in viewModelConstructor!.GetParameters())
                constructorArguments.Add(_serviceProvider.GetService(parameters.ParameterType)!);

            TViewModel viewModel;
            if (constructorArguments.Any())
                viewModel = (TViewModel)Activator.CreateInstance(typeof(TViewModel), constructorArguments.ToArray())!;
            else
                viewModel = (TViewModel)Activator.CreateInstance(typeof(TViewModel))!;

            var page = (BindedPage<TViewModel>)Activator.CreateInstance(pageType)!;
            page.BindingContext = viewModel;

            return (viewModel, page);
        }

        private static Dictionary<Type, Type> GetMappedPopupViewToViewModel()
        {
            try
            {
                var typesAssemblies = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes()).ToList();
                var pages = typesAssemblies
                    .Where(x => NavigationService.IsChildOfGenericType(x, typeof(BindedPage<>)))
                    .ToList();

                return pages.ToDictionary(x => GetGenericTypeParameter(x));
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
            while (!type!.IsGenericType || type.GetGenericTypeDefinition() != typeof(BindedPage<>))
                type = type.BaseType!;
            return type.GetGenericArguments()[0];
        }

        private static BindedViewModel? GetCurrentViewModel(Page? currentPage)
        {
            if (currentPage is ITabViewPage tabViewPage)
            {
                var selectedItem = tabViewPage.TabbedPage.SelectedItem;
                if (selectedItem is Page page)
                    return page.BindingContext as BindedViewModel;
            }

            return currentPage?.BindingContext as BindedViewModel;
        }

        private static Page GetCurrentPage(Page page)
        {
            switch (page)
            {
                case NavigationPage navigationPage:
                    return GetCurrentPage(navigationPage.CurrentPage);

                case MultiPage<Page> multiPage:
                    return GetCurrentPage(multiPage.CurrentPage);
            }

            return page;
        }
    }
}

