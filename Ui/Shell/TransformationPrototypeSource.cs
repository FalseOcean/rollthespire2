using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Shell;

internal sealed record TransformationPrototypeSource(string Id, ModelKey Identity, int ResultCount, ModelKey? OtherRelic = null);
