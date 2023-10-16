using GigMobile.ViewModels.TrustEnforcers;

namespace GigMobile.Pages.TrustEnforcers;

public partial class VerifyNumberPage : BasePage<VerifyNumberViewModel>
{
	public VerifyNumberPage()
	{
		InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, EventArgs e)
    {
        Entry1.Focus();
    }

    void Entry_Focused(System.Object sender, Microsoft.Maui.Controls.FocusEventArgs e)
    {
        var entry = sender as Entry;
        entry.CursorPosition = 0;
    }

    void Entry_TextChanged(System.Object sender, Microsoft.Maui.Controls.TextChangedEventArgs e)
    {
		var entry = sender as Entry;
        entry.Text = !string.IsNullOrEmpty(e.NewTextValue) ? e.NewTextValue[0].ToString() : string.Empty;
        if (!string.IsNullOrEmpty(entry.Text))
        {
            var layout = entry.Parent as Layout;
            var index = layout.Children.IndexOf(entry);
            if (layout.Children.Count > index + 1)
                (layout.Children[index + 1] as Entry)?.Focus();
            else
                ViewModel.SubmitCommand?.Execute(null);
        }
    }
}
