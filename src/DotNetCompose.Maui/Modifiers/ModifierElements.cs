using Microsoft.Maui.Graphics;

namespace DotNetCompose.Maui.Modifiers;

internal abstract record ModifierElement
{
}

internal sealed record SizeElement(double Width, double Height) : ModifierElement
{
}

internal sealed record FillElement(bool Width, bool Height) : ModifierElement
{
}

internal sealed record PaddingElement(double Left, double Top, double Right, double Bottom) : ModifierElement
{
}

internal sealed record BackgroundElement(Color Color) : ModifierElement
{
}

internal sealed record BorderElement(Color Color, double Thickness) : ModifierElement
{
}

internal sealed record DrawBehindElement(Action<MauiDrawScope> Draw) : ModifierElement
{
}

internal sealed record DrawForegroundElement(Action<MauiDrawScope> Draw) : ModifierElement
{
}

internal sealed record DrawWithContentElement(Action<MauiDrawScope> Draw) : ModifierElement
{
}

internal sealed record ClickableElement(Action OnClick, bool Enabled, string? OnClickLabel,
    ClickableRole? Role) : ModifierElement
{
}
