namespace BindedMvvm.Abstractions
{
    public interface ITabItemContentView<TViewModel>
    {
        TViewModel ViewModel { get; }
    }
}

