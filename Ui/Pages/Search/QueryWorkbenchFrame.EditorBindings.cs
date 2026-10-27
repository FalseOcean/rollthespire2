using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Pages.Search.Editors;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private bool _refreshingEditorContexts;
    private readonly HashSet<string> _renderedEditorDomains = new(StringComparer.Ordinal);

    private void OpeningContextChanged()
    {
        if (_refreshingEditorContexts) return;
        using var observation = _editorInputs.Observe(_runtime);
        _renderedEditorDomains.Remove("combat");
        _renderedEditorDomains.Remove("transform");
        RefreshCombatContext(render: _selectedDomain == "combat");
        if (!_multiplayer) RefreshTransformationContext(render: _selectedDomain == "transform");
    }

    private void EventQueueChanged()
    {
        if (!_refreshingEditorContexts)
        {
            _renderedEditorDomains.Remove("transform");
            RefreshTransformationContext(render: _selectedDomain == "transform");
        }
    }

    private void RefreshEditorContexts(bool render = true)
    {
        // A full refresh already owns the final C/T projections. Notifications
        // from intermediate Neow/party/event bindings must not render them again.
        using var observation = _editorInputs.Observe(_runtime);
        _refreshingEditorContexts = true;
        try
        {
            // Bind every semantic context now, but construct only the active
            // page's controls. Hidden-page layout is not an opening prerequisite.
            _renderedEditorDomains.Clear();
            if (RefreshAllEditorContexts(render: false))
            {
                if (render) RenderEditorDomain(_selectedDomain);
                _boundEditorFreshness = _editorInputs.Freshness;
            }
        }
        finally { _refreshingEditorContexts = false; }
    }

    private void RenderEditorDomain(string domain)
    {
        if (_renderedEditorDomains.Contains(domain)) return;
        using var observation = _editorInputs.Observe(_runtime);
        switch (domain)
        {
            case "neow":
                _neowEditor.Refresh(_language, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
                    _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
                    _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, sharedContext: ActiveEditorContext());
                break;
            case "combat": RefreshCombatContext(); break;
            case "shop": RefreshShopContext(); break;
            case "relics": RefreshRelicContext(); break;
            case "ancient": RefreshAncientContext(); break;
            case "events": RefreshEventContext(); break;
            case "transform": RefreshTransformationContext(); break;
            case "boss": case "map":
                if (_multiplayer)
                {
                    if (!TryEditorParty(out var party)) return;
                    var union = _lobbyUnlocks.Take(_playerCount).All(u => u is not null)
                        ? new MegaCrit.Sts2.Core.Unlocks.UnlockState(_lobbyUnlocks.Take(_playerCount)
                            .Select(u => MegaCrit.Sts2.Core.Unlocks.UnlockState.FromSerializable(u!))).ToSerializable() : null;
                    _actInformationEditor.Refresh(_language, _text, _seatCharacters[0], _partyAscension,
                        _playerCount, 0, union, partyWorld: party?.World);
                }
                else RefreshActInformationContext();
                _renderedEditorDomains.Add("boss"); _renderedEditorDomains.Add("map");
                break;
        }
        _renderedEditorDomains.Add(domain);
    }

    private bool RefreshAllEditorContexts(bool render)
    {
        // Never substitute a solo/partial capture after a failed party capture.
        if (!TryEditorParty(out var party)) return false;
        if (_multiplayer)
        {
            for (int seat = 0; seat < _playerCount; seat++)
            {
                _neowEditor.Refresh(_language, _seatCharacters[seat], _partyAscension, _playerCount, seat, _lobbyUnlocks[seat], render: false, sharedContext: EditorContextForSeat(seat));
            }
            _neowEditor.Refresh(_language, _seatCharacters[_seat], _partyAscension, _playerCount, _seat, _lobbyUnlocks[_seat], render: render, sharedContext: ActiveEditorContext());
            var union = _lobbyUnlocks.Take(_playerCount).All(u => u is not null)
                ? new MegaCrit.Sts2.Core.Unlocks.UnlockState(_lobbyUnlocks.Take(_playerCount).Select(u => MegaCrit.Sts2.Core.Unlocks.UnlockState.FromSerializable(u!))).ToSerializable() : null;
            _actInformationEditor.Refresh(_language, _text, _seatCharacters[0], _partyAscension, _playerCount, 0, union, render: render,
                partyWorld: party?.World);
            RefreshCombatContext(render);
            RefreshShopContext(render);
            RefreshRelicContext(render);
            RefreshAncientContext(render);
            RefreshEventContext(render);
            return true;
        }
        _neowEditor.Refresh(_language, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
            _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
            _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, render: render, sharedContext: ActiveEditorContext());
        RefreshCombatContext(render);
        RefreshShopContext(render);
        RefreshRelicContext(render);
        RefreshAncientContext(render);
        RefreshEventContext(render);
        RefreshActInformationContext(render);
        RefreshTransformationContext(render: render);
        return true;
    }

    private void RefreshCombatContext(bool render = true)
    {
        _combatEditor.Refresh(_language, _text, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
            _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
            _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, render: render, sharedContext: ActiveEditorContext());
    }

    private void RefreshShopContext(bool render = true)
    {
        _shopEditor.Refresh(_language, _text, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
            _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
            _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, render: render, sharedContext: ActiveEditorContext());
    }

    private void RefreshRelicContext(bool render = true)
    {
        _relicEditor.Refresh(_language, _text, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
            _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
            _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, render: render, sharedContext: ActiveEditorContext());
    }

    private void RefreshAncientContext(bool render = true)
    {
        if (!TryEditorParty(out var party)) return;
        _ancientEditor.Refresh(_language, _text, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
            _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
            _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, render: render,
                partyAuthority: party?.Players[_seat], sharedContext: ActiveEditorContext());
    }

    private void RefreshEventContext(bool render = true)
    {
        if (!TryEditorParty(out var party)) return;
        _eventEditor.Refresh(_language, _text, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
            _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
            _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, render: render,
                partyAuthority: party?.Players[_seat], sharedContext: ActiveEditorContext());
    }

    private void RefreshActInformationContext(bool render = true)
    {
        _actInformationEditor.Refresh(_language, _text, _multiplayer ? _seatCharacters[_seat] : _soloCharacter,
            _multiplayer ? _partyAscension : _soloAscension, _multiplayer ? _playerCount : 1,
            _multiplayer ? _seat : 0, _multiplayer ? _lobbyUnlocks[_seat] : null, render: render, sharedContext: ActiveEditorContext());
    }

    private void PickTransformationCards(IReadOnlyList<ModelKey> candidates, Action<ModelKey> selected)
    {
        int seat = _multiplayer ? _seat : 0;
        if (candidates.Count == 0 || !_neowEditor.TryTransformationPickerMetadata(seat, out var openingMetadata, out var openingMultiplayer)) return;
        var metadata = openingMetadata.ToDictionary(pair => pair.Key, pair => pair.Value, ModelKeyComparer.Instance);
        foreach (var pair in _eventEditor.TransformationCardPickerMetadata)
            metadata.TryAdd(pair.Key, pair.Value);
        var multiplayer = openingMultiplayer.Concat(_eventEditor.TransformationMultiplayerOnlyCards)
            .ToHashSet(ModelKeyComparer.Instance);
        var context = new CardPickerContext(candidates, metadata, true, "transformation-prototype");
        _objectPicker.Open(new(candidates, selected, RuntimeGameContentNameResolver.Create(_language), _text, _icons, _ => "Other",
            context, multiplayer, _multiplayer ? _playerCount : 1));
    }

    private void RefreshTransformationContext(int? targetSeat = null, bool render = true)
    {
        int seat = targetSeat ?? (_multiplayer ? _seat : 0);
        TransformationSearchSource[] sources = _neowEditor.TransformationSources(seat)
            .Concat(_eventEditor.TransformationSources(seat)).ToArray();
        _transformationEditor.Refresh(_language, _text, seat, sources,
            _neowEditor.TransformationCardCandidates(seat).Concat(_eventEditor.TransformationCardCandidates).ToArray(), render);
    }

}
