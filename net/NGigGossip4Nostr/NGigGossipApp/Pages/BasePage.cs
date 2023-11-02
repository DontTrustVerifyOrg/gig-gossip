using BindedMvvm.Abstractions;
using GigMobile.ViewModels;

namespace GigMobile.Pages
{
    public class BasePage<TViewModel> : BindedPage<TViewModel> where TViewModel : BindedViewModel, IBaseViewModel
    {
        protected Grid _rootLayout;
        private StackLayout _background;
        private ActivityIndicator _activityIndicator;

        public bool HasNavigationBar
        {
            get => (bool)GetValue(HasNavigationBarProperty);
            set => SetValue(HasNavigationBarProperty, value);
        }

        public static readonly BindableProperty HasNavigationBarProperty
            = BindableProperty.Create(nameof(HasNavigationBar), typeof(bool), typeof(BasePage<TViewModel>), true);

        public BasePage()
        {
            NavigationPage.SetHasNavigationBar(this, false);
            BackgroundColor = Colors.White;
        }

        protected override void OnBindingContextChanged()
        {
            base.OnBindingContextChanged();

            var layout = Content as Layout;
            var ignoreSafeArea = layout.IgnoreSafeArea;

            if (!ignoreSafeArea)
                WrapContentInRoot();
        }

        private void WrapContentInRoot()
        {
            var actualContent = Content;
            Content = null;

            _background = new StackLayout { BackgroundColor = Colors.Black, Opacity = 0.3f, Margin = new Thickness(-100) };

            _activityIndicator = new ActivityIndicator { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
            _activityIndicator.SetBinding(ActivityIndicator.IsRunningProperty, new Binding(nameof(IBaseViewModel.IsBusy)));
            _activityIndicator.SetBinding(IsVisibleProperty, new Binding(nameof(IBaseViewModel.IsBusy)));

            _background.SetBinding(IsVisibleProperty, new Binding(nameof(IBaseViewModel.IsBusy)));
            _activityIndicator.SetBinding(ActivityIndicator.IsRunningProperty, new Binding(nameof(IBaseViewModel.IsBusy)));
            _activityIndicator.SetBinding(IsVisibleProperty, new Binding(nameof(IBaseViewModel.IsBusy)));

            Content = _rootLayout = new Grid { actualContent };

            if (HasNavigationBar)
            {
                _rootLayout.RowDefinitions = new RowDefinitionCollection(
                    new RowDefinition[] { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) });

                Grid.SetRowSpan(_activityIndicator, 2);
                Grid.SetRowSpan(_background, 2);
                Grid.SetRow(actualContent, 1);

                var backButton = new ImageButton
                {
                    Source = "back_arrow",
                    BorderWidth = 0,
                    HeightRequest = 24,
                    WidthRequest = 24,
                    HorizontalOptions = LayoutOptions.Start,
                    Margin = new Thickness(16)
                };
                backButton.SetBinding(Button.CommandProperty, new Binding(nameof(IBaseViewModel.BackCommand)));

                _rootLayout.Add(new StackLayout { backButton });
            }

            _rootLayout.Add(_activityIndicator);
            _rootLayout.Add(_background);
        }
    }
}

