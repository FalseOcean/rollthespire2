using Godot;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Pages;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class PageHost : MarginContainer
{
    private readonly Dictionary<AppPageKey, IAppPage> _pages = new();
    private readonly Dictionary<AppPageKey, Func<IAppPage>> _factories = new();
    private IAppPage? _active;

    public PageHost()
    {
        AddThemeConstantOverride("margin_left", 24);
        AddThemeConstantOverride("margin_top", 20);
        AddThemeConstantOverride("margin_right", 24);
        AddThemeConstantOverride("margin_bottom", 20);
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;
    }

    public AppPageKey? ActivePageKey => _active?.PageKey;
    public IAppPage? ActivePage => _active;
    public IEnumerable<IAppPage> CachedPages => _pages.Values;

    public void Register(AppPageKey key, Func<IAppPage> factory) => _factories[key] = factory;

    public void ReleaseFocusFromActivePage()
    {
        if (_active is null || !IsInsideTree())
        {
            return;
        }

        Control? focusOwner = GetViewport().GuiGetFocusOwner();
        if (focusOwner is not null && _active.View.IsAncestorOf(focusOwner))
        {
            focusOwner.ReleaseFocus();
        }
    }

    public IAppPage Activate(AppPageKey key)
    {
        if (!_pages.TryGetValue(key, out IAppPage? page))
        {
            if (!_factories.TryGetValue(key, out Func<IAppPage>? factory))
            {
                throw new InvalidOperationException($"No page factory is registered for {key}.");
            }
            page = factory();
            _pages[key] = page;
            AddChild(page.View);
            page.View.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        }

        foreach (IAppPage candidate in _pages.Values)
        {
            candidate.View.Visible = ReferenceEquals(candidate, page);
        }
        _active = page;
        return page;
    }

    public void ApplyLocalization(IUiTextProvider uiText, IGameContentNameResolver contentNames)
    {
        foreach (IAppPage page in _pages.Values)
        {
            page.ApplyLocalization(uiText, contentNames);
        }
    }

    public void ApplyDisplayMode(AppDisplayMode mode)
    {
        foreach (IAppPage page in _pages.Values)
        {
            page.ApplyDisplayMode(mode);
        }
    }
}
