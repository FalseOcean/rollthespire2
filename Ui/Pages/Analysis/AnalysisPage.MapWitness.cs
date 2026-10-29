using Godot;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private readonly Dictionary<int, (Control Row, Button Open, Label Status)> _mapWitnessEntries = [];
    private SeedPredictionRequest? _mapWitnessRequest;

    private void AddMapWitnessEntry(Node parent, int act)
    {
        var row = new VBoxContainer { Name = "MapWitnessEntry" + act, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var open = WAction("ui1.map.witness.open");
        open.Name = "OpenMapWitness" + act;
        var status = WNote(""); status.Name = "MapWitnessStatus" + act;
        row.AddChild(open); row.AddChild(status); parent.AddChild(row);
        _mapWitnessEntries[act] = (row, open, status);
        open.Pressed += () => OpenMapWitness(act, open);
        RefreshMapWitnessEntries();
    }

    private (PersistedMapRouteWitness? Witness, string Issue) MapWitnessForAct(int act)
    {
        if (_resultOrigin is not { } source) return (null, "");
        // Provenance alone is not a map witness. Ordinary results, legacy
        // results and receipts for another Act have no witness entry here.
        var matches = source.MapRouteWitnesses.Where(witness => witness.Act == act).Distinct().ToArray();
        if (matches.Length == 0) return (null, "");
        if (!source.AppliesToCurrentContext || LastDocument is null ||
            !string.Equals(source.SourceSeed, LastDocument.CanonicalSeed, StringComparison.OrdinalIgnoreCase))
            return (null, "context");
        if (source.IsUnverified || source.ValidationDisposition != SearchDisposition.Match) return (null, "unverified");
        return matches.Length switch
        {
            1 => (matches[0], ""),
            _ => (null, "conflicting")
        };
    }

    private void RefreshMapWitnessEntries()
    {
        foreach (var (act, entry) in _mapWitnessEntries)
        {
            if (!IsInstanceValid(entry.Row)) continue;
            var witness = MapWitnessForAct(act);
            entry.Row.Visible = witness.Witness is not null || witness.Issue.Length > 0;
            entry.Open.Text = Text("ui1.map.witness.open");
            entry.Open.Disabled = witness.Witness is null;
            entry.Status.Visible = witness.Issue.Length > 0;
            entry.Status.Text = witness.Issue.Length > 0 ? Text("ui1.map.witness." + witness.Issue) : "";
        }
    }

    private void OpenMapWitness(int act, Control opener)
    {
        if (MapWitnessForAct(act).Witness is not { } witness || LastDocument is null || _uiText is null) return;
        _mapWitnessRequest = LastRequest;
        _workbenchRoot!.Hide();
        _mapWorkspace.OpenResultWitness(act, LastDocument.CanonicalSeed, _uiText, opener, witness);
    }
}
