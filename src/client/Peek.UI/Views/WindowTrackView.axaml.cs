using AsyncNavigation.Abstractions;
using Avalonia.Controls;
using Avalonia.Controls.DataGridHierarchical;
using Peek.Core.ViewModels;

namespace Peek.UI.Views;

public partial class WindowTrackView : UserControl, IView
{
    private WindowTrackViewModel? _vm = null;
    public WindowTrackView()
    {
        InitializeComponent();    
    }

    public HierarchicalModel<WindowItemViewModel>? Model
    {
        get;
        private set;
    }
    protected override void OnInitialized()
    {
        base.OnInitialized();
        if (DataContext is WindowTrackViewModel vm)
        {
            _vm = vm;
            var options = new HierarchicalOptions<WindowItemViewModel>
            {
                ChildrenSelector = item => item.Children,
                IsLeafSelector = item => item.Children.Count == 0,
                IsExpandedSelector = item => item.IsExpanded,
                IsExpandedSetter = (item, value) => item.IsExpanded = value,
                AutoExpandRoot = true,
                MaxAutoExpandDepth = 2,
                VirtualizeChildren = true
            };
            Model = new HierarchicalModel<WindowItemViewModel>(options);
            Model.SetRoots(_vm.Roots);
            Tree.HierarchicalModel = Model;

            Model.NodeExpanded += Model_NodeExpanded;
        }

    }

    private void Model_NodeExpanded(object? sender, HierarchicalNodeEventArgs e)
    {
        if (_vm is not null)
        {
            
        }
    }
}