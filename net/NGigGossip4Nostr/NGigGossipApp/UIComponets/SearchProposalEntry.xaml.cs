using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace GigMobile.UIComponets;

public partial class SearchProposalEntry : ContentView
{
    private CancellationTokenSource _cancellationTokenSource;

    public SearchProposalEntry()
	{
		InitializeComponent();

        _entry.Unfocused += OnUnfocused;
        _entry.TextChanged += OnTextChanged;
    }

    private async void OnTextChanged(object sender, TextChangedEventArgs e)
    { 
        if (QueryFunc != null && _entry.IsFocused)
        {
            _cancellationTokenSource?.Cancel();

            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                var result = await QueryFunc.Invoke(e.NewTextValue, _cancellationTokenSource.Token);

                if (_entry.IsFocused)
                {
                    BindableLayout.SetItemsSource(_bdStack, result);
                    _proposal.IsVisible = result.Any();
                }
            }
            catch (TaskCanceledException) { }
        }
    }

    public static readonly BindableProperty SelectValueCommandProperty =
        BindableProperty.Create(nameof(SelectValueCommand), typeof(ICommand), typeof(SearchProposalEntry));

    public ICommand SelectValueCommand
    {
        get => (ICommand)GetValue(SelectValueCommandProperty);
        set => SetValue(SelectValueCommandProperty, value);
    }

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(SearchProposalEntry), null);

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(SearchProposalEntry), null);

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set
        {
            SetValue(TextProperty, value);
            _entry.Text = value;
        }
    }

    public static readonly BindableProperty QueryFuncProperty =
        BindableProperty.Create(nameof(QueryFunc), typeof(Func<string, CancellationToken, Task<KeyValuePair<string, object>[]>>), typeof(SearchProposalEntry), null);

    public Func<string, CancellationToken, Task<KeyValuePair<string, object>[]>> QueryFunc
    {
        get => (Func<string, CancellationToken, Task<KeyValuePair<string, object>[]>>)GetValue(QueryFuncProperty);
        set => SetValue(QueryFuncProperty, value);
    }

    public static readonly BindableProperty RootProperty =
        BindableProperty.Create(nameof(Root), typeof(Grid), typeof(SearchProposalEntry), null);

    public Grid Root
    {
        get => (Grid)GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    private void OnUnfocused(object sender, FocusEventArgs e)
    {
        _entry.Text = string.Empty;

        _proposal.IsVisible = false;
        BindableLayout.SetItemsSource(_bdStack, Array.Empty<KeyValuePair<string, object>>());
    }

    protected override void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(Text))
            _entry.Text = Text;
        else if (propertyName == nameof(Root))
        {
            (_proposal.Parent as Layout).Remove(_proposal);
            Root.Add(_proposal);
            Grid.SetRow(_proposal, 0);
            _proposal.Margin = new Thickness(30, 80, 30, 70);
            _proposal.VerticalOptions = LayoutOptions.Start;
            _proposal.HorizontalOptions = LayoutOptions.Fill;
        }
    }
}
