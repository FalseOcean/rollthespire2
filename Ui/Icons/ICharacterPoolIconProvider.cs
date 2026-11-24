using Godot;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Icons;

/// <summary>
/// Main-thread-only provider for the small character pool icons defined by the
/// source-audited native Card Library resources. It never enters Search/Analysis worker contracts.
/// </summary>
internal interface ICharacterPoolIconProvider : IDisposable
{
    IconDescriptor Resolve(ModelKey key);

    /// <summary>
    /// Returns only the native Card Library ColorlessPool visual resource for use
    /// as the visual of the picker's AllAllowedCharacters state. This is not a
    /// colorless-card business filter and never enters Search contracts.
    /// </summary>
    Texture2D? ResolveColorlessPoolVisual();


    void Invalidate();
}
