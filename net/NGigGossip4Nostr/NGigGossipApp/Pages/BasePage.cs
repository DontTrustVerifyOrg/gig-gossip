using BindedMvvm.Abstractions;
using GigMobile.ViewModels;

namespace GigMobile.Pages
{
    public class BasePage<TViewModel> : BindedPage<TViewModel> where TViewModel : BindedViewModel, IBaseViewModel
    {
        public BasePage()
        {
            BackgroundColor = Color.FromArgb("#161b22");
        }
    }
}

