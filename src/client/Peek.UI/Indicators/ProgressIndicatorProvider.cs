using AsyncNavigation;
using AsyncNavigation.Avalonia;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Peek.UI.Views;
using System;

namespace Peek.UI.Indicators
{
    internal class ProgressIndicatorProvider : IInnerIndicatorProvider
    {
        private readonly IServiceProvider _serviceProvider;
        public ProgressIndicatorProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }
        public Control GetErrorIndicator(NavigationContext navigationContext)
        {
            return BuildErrorIndicator(_serviceProvider, navigationContext);
        }

        public Control GetLoadingIndicator(NavigationContext navigationContext)
        {
            return BuildLoadingIndicator(_serviceProvider, navigationContext);
        }

        public bool HasErrorIndicator(NavigationContext navigationContext)
        {
            return true;
        }

        public bool HasLoadingIndicator(NavigationContext navigationContext)
        {
            return true;
        }

        private static LoadingIndicatorView BuildLoadingIndicator(IServiceProvider sp, NavigationContext navigationContext)
        {
            var view = sp.GetRequiredService<LoadingIndicatorView>();
            view.DataContext = navigationContext;
            return view;
        }

        private static LoadingIndicatorView BuildErrorIndicator(IServiceProvider sp, NavigationContext navigationContext)
        {
            return sp.GetRequiredService<LoadingIndicatorView>();
        }
    }
}
