using BindedMvvm.Abstractions;
using GigMobile.ViewModels;

namespace GigMobile.Pages
{
    public class BasePage<TViewModel> : BindedPage<TViewModel> where TViewModel : BindedViewModel, IBaseViewModel
    {
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

            Loaded += BasePage_Loaded;
        }

        protected void BasePage_Loaded(object sender, EventArgs e)
        {
            BackgroundColor = Colors.White;
            /*Background = new LinearGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb("#FFFFFF"), 0.0f),
                    new GradientStop(Color.FromArgb("#FEFEE2"), 1.0f)
                }
            };*/

            var actualContent = Content;
            Content = null;

            var background = new StackLayout { BackgroundColor = Colors.Black, Opacity = 0.3f, Margin = new Thickness(-100) };
            background.SetBinding(IsVisibleProperty, new Binding(nameof(ViewModel.IsBusy)));

            var activityIndicator = new ActivityIndicator { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
            activityIndicator.SetBinding(ActivityIndicator.IsRunningProperty, new Binding(nameof(ViewModel.IsBusy)));

            var rootLayout = new Grid { actualContent };

            if (HasNavigationBar)
            { 
                rootLayout.RowDefinitions = new RowDefinitionCollection(
                    new RowDefinition[] { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } );

                Grid.SetRowSpan(activityIndicator, 2);
                Grid.SetRowSpan(background, 2);
                Grid.SetRow(actualContent, 1);

                var backButton = new ImageButton
                {
                    Source = "back_arrow",
                    BorderWidth = 0,
                    HeightRequest = 24,
                    WidthRequest = 24,
                    HorizontalOptions = LayoutOptions.Start,
                    Margin = new Thickness (16, 0)
                };
                backButton.SetBinding(Button.CommandProperty, new Binding(nameof(ViewModel.BackCommand)));

                rootLayout.Add(new StackLayout { backButton });
            }

            rootLayout.Add(activityIndicator);
            rootLayout.Add(background);

            Content = rootLayout;
        }
    }
}

