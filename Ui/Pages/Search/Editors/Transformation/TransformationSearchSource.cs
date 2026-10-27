using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed record TransformationSearchSource(string Id, ModelKey Identity, int ResultCount, ModelKey? OtherRelic = null);
