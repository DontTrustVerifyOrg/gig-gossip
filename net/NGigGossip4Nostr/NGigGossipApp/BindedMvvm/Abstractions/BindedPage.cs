using System;
namespace BindedMvvm.Abstractions
{
	public abstract class BindedPage<TViewModel> : ContentPage where TViewModel : BindedViewModel
    {
        public TViewModel? ViewModel => BindingContext as TViewModel;

        protected override void OnAppearing()
        {
            base.OnAppearing();
            ViewModel!.OnAppearing();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            ViewModel!.OnDisappearing();
        }
    }
}

