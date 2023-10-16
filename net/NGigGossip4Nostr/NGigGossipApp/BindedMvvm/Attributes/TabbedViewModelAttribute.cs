using System;
namespace BindedMvvm.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class TabbedViewModelAttribute : Attribute
    {
        public int TabIndex { get; }
        public Type ParentViewModel { get; }

        public TabbedViewModelAttribute(Type parentViewModel, int tabIndex)
        {
            ParentViewModel = parentViewModel;
            TabIndex = tabIndex;
        }
    }
}

