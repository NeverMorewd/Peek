using ReactiveUI;
using System;
using System.Reactive;

using Peek.Core.Models;

namespace Peek.Core.ViewModels;

public class PaletteSwatchViewModel : ReactiveObject
{
    public ColorModel Color { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public event Action<ColorModel>? Selected;

    public ReactiveCommand<Unit, Unit> SelectCommand { get; }

    public PaletteSwatchViewModel(ColorModel color)
    {
        Color = color;

        SelectCommand = ReactiveCommand.Create(() =>
        {
            Selected?.Invoke(Color);
        });
    }

    public void UpdateSelected(ColorModel selected)
    {
        IsSelected = selected == Color;
    }
}