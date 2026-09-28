using Godot;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class QueryWorkbenchFrame
{
    private void UpdateDomainCounts()
    {
        if (_domainCountLabels.Count == 0) return;
        var counts = new (string Id, int Count)[]
        {
            ("neow", _neowEditor.AuthoredConditionCount()),
            ("ancient", _ancientEditor.AuthoredConditionCount()),
            ("shop", _shopEditor.AuthoredConditionCount()),
            ("combat", _combatEditor.AuthoredConditionCount()),
            ("events", _eventEditor.AuthoredConditionCount()),
            ("boss", _actInformationEditor.BossVariantConditionCount()),
            ("map", _actInformationEditor.MapConditionCount()),
            ("relics", _relicEditor.AuthoredConditionCount()),
            ("transform", _multiplayer ? 0 : _transformationEditor.AuthoredConditionCount())
        };
        foreach ((string id, int count) in counts)
        {
            if (!_domainCountLabels.TryGetValue(id, out Label? label)) continue;
            string value = count > 0 ? count.ToString() : string.Empty;
            if (label.Text != value) label.Text = value;
            label.Visible = count > 0;
            label.Modulate = _domainButtons[id].Disabled ? new Color(1f, 1f, 1f, .5f) : Colors.White;
        }
    }
}
