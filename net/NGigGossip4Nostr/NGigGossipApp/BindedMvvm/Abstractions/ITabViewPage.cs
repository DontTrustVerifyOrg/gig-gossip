namespace BindedMvvm.Abstractions
{
    public interface ITabViewPage
    {
        TabbedPage TabbedPage { get; }

        void SelectTabIndex(int index);
    }
}

