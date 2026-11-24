using System.Collections;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Infrastructure.Snapshots.RuntimeAuthority;

internal sealed record CharacterStartingDeckSnapshot(
    ModelKey CharacterKey,
    IReadOnlyList<NeowEffectCardSnapshot> OrderedDeck);

internal static partial class RuntimeAuthoritySnapshotCapture
{
    // Environment-only evidence: extending the persisted environment identity must
    // not silently change schema 1's verified vanilla compatibility fingerprint.
    internal static IReadOnlyList<CharacterStartingDeckSnapshot>? CaptureCharacterStartingDecks()
    {
        using var observation = new CharacterStartingDeckObservation();
        while (observation.MoveNext()) { }
        return observation.Result;
    }

    internal static IReadOnlyList<CharacterStartingDeckSnapshot>? CaptureCharacterStartingDecks(Type modelDb)
    {
        using var observation = new CharacterStartingDeckObservation(modelDb);
        while (observation.MoveNext()) { }
        return observation.Result;
    }

    // Each step reads at most one character's deck getter or one card's metadata.
    // The synchronous and sliced consumers deliberately use the same conversion.
    internal sealed class CharacterStartingDeckObservation(Type? modelDb = null) : IDisposable
    {
        private IReadOnlyList<object>? _characters;
        private readonly List<CharacterStartingDeckSnapshot> _output = [];
        private readonly HashSet<ModelKey> _seen = new(ModelKeyComparer.Instance);
        private List<NeowEffectCardSnapshot> _deck = [];
        private IEnumerator? _cards;
        private ModelKey _key;
        private int _character;
        private bool _completed;
        internal IReadOnlyList<CharacterStartingDeckSnapshot>? Result { get; private set; }

        internal bool MoveNext()
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            if (_completed) return false;
            try
            {
                if (_characters is null)
                {
                    if (modelDb is null)
                    {
                        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate =>
                            string.Equals(candidate.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase));
                        modelDb = assembly is null ? null : FindType(assembly, "MegaCrit.Sts2.Core.Models.ModelDb", "ModelDb");
                    }
                    if (modelDb is null || !TryReadStaticEnumerable(modelDb, "AllCharacters", out var characters))
                        return Fail();
                    _characters = characters;
                    return true;
                }
                if (_cards is not null)
                {
                    if (!_cards.MoveNext())
                    {
                        if (_deck.Count == 0) return Fail();
                        (_cards as IDisposable)?.Dispose(); _cards = null;
                        _output.Add(new(_key, _deck.ToArray()));
                        return true;
                    }
                    if (_cards.Current is not { } card) return Fail();
                    var converted = ReflectionNeowEffectSnapshotAdapter.ConvertCardModel(
                        card, _deck.Count, "character", $"starter:{_key.Entry}:{_deck.Count}");
                    if (converted is null) return Fail();
                    _deck.Add(converted);
                    return true;
                }
                if (_character == _characters.Count)
                { Result = _output.ToArray(); _completed = true; return false; }
                object character = _characters[_character++];
                if (!TryModelKey(character, out _key) || !_seen.Add(_key) ||
                    character.GetType().GetProperty("StartingDeck", PublicInstance)?.GetValue(character) is not IEnumerable source)
                    return Fail();
                _deck = []; _cards = source.GetEnumerator();
                return true;
            }
            catch
            {
                // Missing deck evidence makes identity unavailable; it must never
                // turn a partial capture into a comparable environment.
                return Fail();
            }
        }

        private bool Fail()
        {
            _completed = true; Result = null;
            try { (_cards as IDisposable)?.Dispose(); } catch { }
            _cards = null;
            return false;
        }

        public void Dispose()
        {
            _completed = true;
            (_cards as IDisposable)?.Dispose(); _cards = null;
        }
    }
}
