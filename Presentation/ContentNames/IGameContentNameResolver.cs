using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Presentation.ContentNames;

public enum GameContentKind
{
    Relic,
    Card,
    Potion,
    Character,
    Event,
    Encounter,
    Ancient,
    Act
}

public interface IGameContentNameResolver
{
    string LanguageCode { get; }
    string Resolve(ModelKey key, GameContentKind kind);
}
