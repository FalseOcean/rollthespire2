# Temporary Analysis Ancient Geometry Probe

This is **not production instrumentation** and is not compiled by `RolltheSpire2.csproj`.
Use only for the Owner 1600×900 visual/geometry smoke if exact rendered rectangles are needed.

The production Ancient Group Card names the relevant Controls so a one-shot probe can locate them without changing prediction/UI semantics:

```text
AnalysisAncientGroupCard
  ConditionsRow
  Act2Row
    ActLabel
    AncientIdentityColumn
    OptionsColumns
      Option1
      Option2
      Option3
  Act3Row
    ...same columns...
```

At **AppShell logical size exactly 1600×900**, after the Analysis document has rendered, temporarily invoke equivalent main-thread code:

```csharp
static string Rect(Control c) =>
    $"name={c.Name};global=({c.GlobalPosition.X:F1},{c.GlobalPosition.Y:F1});" +
    $"size=({c.Size.X:F1},{c.Size.Y:F1});min=({c.CustomMinimumSize.X:F1},{c.CustomMinimumSize.Y:F1})";

Control group = (Control)FindChild("AnalysisAncientGroupCard", recursive: true, owned: false);
GD.Print("RT2_ANCIENT_GEOMETRY " + Rect(group));
foreach (string rowName in new[] { "ConditionsRow", "Act2Row", "Act3Row" })
{
    Control row = (Control)group.FindChild(rowName, recursive: true, owned: false);
    GD.Print("RT2_ANCIENT_GEOMETRY " + Rect(row));
    if (rowName == "ConditionsRow") continue;
    foreach (string childName in new[] { "ActLabel", "AncientIdentityColumn", "OptionsColumns", "Option1", "Option2", "Option3" })
    {
        Control? child = row.FindChild(childName, recursive: true, owned: false) as Control;
        if (child is not null) GD.Print("RT2_ANCIENT_GEOMETRY row=" + rowName + ";" + Rect(child));
    }
}
```

Run once after two deferred layout passes. Remove the temporary call after capturing the lines. Do not keep the probe in ordinary Production logging.

Also capture one Chinese-localization screenshot with Act2/Act3 both present and three named options; include a target-identity case if available.
