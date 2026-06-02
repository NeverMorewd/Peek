using Avalonia.Controls;
using Avalonia.Media;
using Peek.Core.Abstractions;
using Peek.Core.Models;

namespace Peek.Views;

public partial class MainWindow : Window, IColorChangedNotify
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public void ChangeColor(ColorModel colorModel)
    {
        Pipboy.Avalonia.PipboyThemeManager.Instance.SetPrimaryColor(Color.FromRgb(colorModel.R, colorModel.G, colorModel.B));
    }
}