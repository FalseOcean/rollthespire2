using Godot;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Pages;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.SaveStatus;

/// <summary>
/// Read-only current game/save/environment inspector. It consumes only the RT2-owned
/// immutable RuntimeAuthorityEnvironment snapshot captured on the Godot main thread.
/// Fingerprint/baseline state is diagnostic/provenance evidence and never a Search gate.
/// </summary>
internal sealed partial class SaveStatusPage : MarginContainer, IAppPage
{
    private readonly ModRuntimeSnapshot _runtime;
    private readonly Label _pageTitle;
    private readonly Label _subtitle;
    private readonly VBoxContainer _gameRows;
    private readonly Label _semanticSummary;
    private readonly Label _semanticMessage;
    private readonly VBoxContainer _semanticRows;
    private readonly Label _unlockSummary;
    private readonly Label _unlockMessage;
    private readonly VBoxContainer _unlockRows;
    private readonly Label _fingerprintMeta;
    private readonly VBoxContainer _deviceRows;
    private readonly List<(Button Header, Control Body, string Key)> _sections = new();

    public SaveStatusPage(ModRuntimeSnapshot runtime)
    {
        _runtime = runtime ?? ModRuntimeSnapshot.NotInitialized;
        PageKey = AppPageKey.SaveStatus;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var root = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 14);
        _pageTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.PageTitle);
        _subtitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        root.AddChild(_pageTitle);
        root.AddChild(_subtitle);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 14);

        (_deviceRows, _) = CreateSection(content);
        (_gameRows, _) = CreateSection(content);
        _semanticRows = CreateSectionWithHeader(content, out _semanticSummary, out _semanticMessage);
        _unlockRows = CreateSectionWithHeader(content, out _unlockSummary, out _unlockMessage);

        var fingerprintPanel = new PanelContainer();
        Ui1Theme.ApplyPanel(fingerprintPanel, Ui1SurfaceRole.Card, 4f, 1, 14f);
        _fingerprintMeta = Ui1Theme.Label(string.Empty, Ui1TextRole.Code, true);
        _fingerprintMeta.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        fingerprintPanel.AddChild(_fingerprintMeta);
        content.AddChild(fingerprintPanel);

        string[] titles = ["ui1.devices.title", Ui1TextKey.SaveStatusGameModelTitle,
            Ui1TextKey.SaveStatusSemanticTitle, Ui1TextKey.SaveStatusUnlockTitle, "ui1.save_status.details"];
        for (int i = 0; i < content.GetChildCount(); i++)
        {
            var panel = content.GetChild<PanelContainer>(i);
            var body = panel.GetChild<Control>(0);
            panel.RemoveChild(body);
            var section = new VBoxContainer();
            var header = new Button { ToggleMode = true, ButtonPressed = true, Alignment = HorizontalAlignment.Left };
            Ui1Theme.ApplyButton(header, Ui1ButtonRole.Secondary);
            header.Toggled += expanded => {
                body.Visible = expanded;
                header.Text = (expanded ? "▾ " : "▸ ") + header.Text[2..];
            };
            section.AddChild(header);
            section.AddChild(body);
            panel.AddChild(section);
            _sections.Add((header, body, titles[i]));
        }
        _semanticSummary.Visible = false;
        _unlockSummary.Visible = false;

        scroll.AddChild(content);
        root.AddChild(scroll);
        AddChild(root);
    }

    public AppPageKey PageKey { get; }
    public Control View => this;

    public void ApplyLocalization(IUiTextProvider uiText, IGameContentNameResolver contentNames)
    {
        _ = contentNames;
        _pageTitle.Text = uiText.Get(Ui1TextKey.SaveStatusTitle);
        _subtitle.Text = uiText.Get(Ui1TextKey.SaveStatusSubtitle);

        RuntimeAuthorityEnvironmentSnapshot environment = RuntimeAuthorityEnvironment.Current;
        RuntimeAuthoritySnapshot authority = environment.Authority;
        RuntimeAuthorityInterpretation interpretation = environment.Interpretation;

        RebuildGameRows(uiText, authority);
        RebuildDeviceRows(uiText);
        RebuildSemanticRows(uiText, authority, interpretation);
        RebuildUnlockRows(uiText, authority, interpretation);
        RebuildFingerprintMeta(uiText, environment);
        foreach (var section in _sections)
            section.Header.Text = (section.Body.Visible ? "▾ " : "▸ ") + uiText.Get(section.Key);
    }

    public void ApplyDisplayMode(AppDisplayMode mode) => _ = mode;

    private void RebuildDeviceRows(IUiTextProvider text)
    {
        ClearRows(_deviceRows);
        var identity = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        var device = FamilyDeviceProfileFoundation.Capture();
        AddKeyValue(_deviceRows, "CPU", identity.CpuIdentity);
        AddKeyValue(_deviceRows, text.Get("ui1.devices.cpu_usage"), text.Get("ui1.devices.cpu_available"));
        var cpu = CpuCostCalibration.DisplaySummary();
        if (cpu.Length == 0)
            AddKeyValue(_deviceRows, text.Get("ui1.devices.relative"), text.Get("ui1.devices.reference_default"));
        foreach (var row in cpu)
            AddKeyValue(_deviceRows, text.Format("ui1.devices.cpu_workers", row.Workers),
                text.Format("ui1.devices.factor", 1 / row.Ratio, row.Samples));
        AddKeyValue(_deviceRows, "GPU", identity.HasKnownGpu ? identity.GpuIdentity : text.Get("ui1.devices.unidentified"));
        string state = !FamilyDeviceProfileFoundation.GpuAvailable ? "ui1.devices.unavailable" :
            device.FamilyComputeRuntimeObserved ? "ui1.devices.observed" : "ui1.devices.unverified";
        AddKeyValue(_deviceRows, text.Get("ui1.devices.gpu_usage"), text.Get(state));
        var gpu = GpuCostCalibration.DisplaySummary();
        AddKeyValue(_deviceRows, text.Get("ui1.devices.relative"), gpu.Comparable > 0
            ? text.Format("ui1.devices.factor", 1 / gpu.Ratio, gpu.Comparable)
            : text.Get("ui1.devices.reference_default"));
        AddKeyValue(_deviceRows, text.Get("ui1.devices.recorded"), gpu.Recorded.ToString());
        AddKeyValue(_deviceRows, text.Get("ui1.devices.backend"), string.IsNullOrEmpty(identity.RenderingBackend) ? "—" : identity.RenderingBackend);
        var help = Ui1Theme.Label(text.Get("ui1.devices.help"), Ui1TextRole.Meta, true);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _deviceRows.AddChild(help);
    }

    private void RebuildGameRows(IUiTextProvider uiText, RuntimeAuthoritySnapshot authority)
    {
        ClearRows(_gameRows);
        AddKeyValue(_gameRows, uiText.Get(Ui1TextKey.SaveStatusGameVersion),
            string.IsNullOrWhiteSpace(authority.GameVersion) ? _runtime.Detection.DisplayVersion : authority.GameVersion);
        AddKeyValue(_gameRows, uiText.Get(Ui1TextKey.SaveStatusPredictionModel), _runtime.Profile.ProfileId.ToString());
        AddKeyValue(_gameRows, uiText.Get(Ui1TextKey.SaveStatusModelStatus), ResolveModelStatus(uiText));
        AddKeyValue(_gameRows, uiText.Get(Ui1TextKey.SaveStatusFingerprintSchema), authority.FingerprintSchemaVersion.ToString());
    }

    private void RebuildSemanticRows(
        IUiTextProvider uiText,
        RuntimeAuthoritySnapshot authority,
        RuntimeAuthorityInterpretation interpretation)
    {
        ClearRows(_semanticRows);
        _semanticSummary.Text = uiText.Get(Ui1TextKey.SaveStatusSemanticTitle);
        (_semanticMessage.Text, Color color) = interpretation.EnvironmentStatus switch
        {
            SemanticEnvironmentStatus.VerifiedVanillaMatch =>
                (uiText.Format(Ui1TextKey.SaveStatusSemanticVerified, interpretation.MatchedBaselineGameVersion), Ui1Theme.Palette.Success),
            SemanticEnvironmentStatus.KnownBaselineMatchButGameVersionUnverified =>
                (uiText.Format(Ui1TextKey.SaveStatusSemanticCrossVersionMatch, interpretation.MatchedBaselineGameVersion), Ui1Theme.Palette.Partial),
            SemanticEnvironmentStatus.SemanticMismatch when interpretation.SameGameVersionBaselineKnown =>
                (uiText.Format(Ui1TextKey.SaveStatusSemanticMismatchSameVersion, interpretation.ComparisonBaselineGameVersion), Ui1Theme.Palette.Warning),
            SemanticEnvironmentStatus.SemanticMismatch =>
                (uiText.Get(Ui1TextKey.SaveStatusSemanticMismatchUnknownVersion), Ui1Theme.Palette.Warning),
            _ when !authority.Fingerprint.Complete =>
                (uiText.Get(Ui1TextKey.SaveStatusSemanticFingerprintIncomplete), Ui1Theme.Palette.Unknown),
            _ =>
                (uiText.Get(Ui1TextKey.SaveStatusSemanticAwaitingBaseline), Ui1Theme.Palette.Unknown)
        };
        _semanticSummary.AddThemeColorOverride("font_color", color);

        var comparisons = interpretation.DomainComparisons.ToDictionary(item => item.Domain, StringComparer.Ordinal);
        foreach (RuntimeSemanticDomainSnapshot domain in authority.SemanticUniverse)
        {
            string hash = authority.Fingerprint.GetDomainHash(domain.Domain);
            string state = !domain.AuthorityComplete || string.IsNullOrWhiteSpace(hash)
                ? uiText.Get(Ui1TextKey.SaveStatusDomainUnavailable)
                : comparisons.TryGetValue(domain.Domain, out RuntimeAuthorityDomainComparison? comparison)
                    ? comparison.Status switch
                    {
                        RuntimeAuthorityDomainComparisonStatus.Match => uiText.Get(Ui1TextKey.SaveStatusDomainMatch),
                        RuntimeAuthorityDomainComparisonStatus.Mismatch => uiText.Get(Ui1TextKey.SaveStatusDomainMismatch),
                        _ => uiText.Get(Ui1TextKey.SaveStatusDomainCaptured)
                    }
                    : uiText.Get(Ui1TextKey.SaveStatusDomainCaptured);
            string value = uiText.Format(
                Ui1TextKey.SaveStatusDomainValue,
                state,
                domain.Count,
                ShortHash(hash));
            AddKeyValue(_semanticRows, LocalizeDomain(uiText, domain.Domain), value,
                comparisons.TryGetValue(domain.Domain, out RuntimeAuthorityDomainComparison? c) && c.Status == RuntimeAuthorityDomainComparisonStatus.Mismatch
                    ? Ui1Theme.Palette.Warning
                    : null);
        }
    }

    private void RebuildUnlockRows(
        IUiTextProvider uiText,
        RuntimeAuthoritySnapshot authority,
        RuntimeAuthorityInterpretation interpretation)
    {
        ClearRows(_unlockRows);
        _unlockSummary.Text = uiText.Get(Ui1TextKey.SaveStatusUnlockTitle);
        (_unlockMessage.Text, Color color) = interpretation.VanillaUnlockStatus switch
        {
            VanillaUnlockOverallStatus.Full => (uiText.Get(Ui1TextKey.SaveStatusUnlockFull), Ui1Theme.Palette.Success),
            VanillaUnlockOverallStatus.Partial => (uiText.Get(Ui1TextKey.SaveStatusUnlockPartial), Ui1Theme.Palette.Partial),
            _ => (uiText.Get(Ui1TextKey.SaveStatusUnlockUnknown), Ui1Theme.Palette.Unknown)
        };
        _unlockSummary.AddThemeColorOverride("font_color", color);

        if (interpretation.VanillaUnlockCoverage.Count > 0)
        {
            foreach (VanillaUnlockDomainCoverage coverage in interpretation.VanillaUnlockCoverage)
            {
                string value = coverage.Complete
                    ? uiText.Format(Ui1TextKey.SaveStatusUnlockCoverage, coverage.Unlocked, coverage.VanillaUniverse)
                    : uiText.Get(Ui1TextKey.SaveStatusDomainUnavailable);
                AddKeyValue(_unlockRows, LocalizeDomain(uiText, coverage.Domain), value,
                    coverage.Complete && !coverage.IsFull ? Ui1Theme.Palette.Partial : null);
            }
            return;
        }

        // Before the first Owner-verified baseline exists we do not call the runtime
        // all-unlocks universe "vanilla". Show source availability only, without turning
        // current mod-added unlockables into the definition of vanilla completion.
        foreach (RuntimeUnlockAuthorityDomainSnapshot domain in authority.UnlockAuthority)
        {
            RuntimeCurrentUnlockDomainSnapshot? current = authority.FindCurrentUnlockDomain(domain.Domain);
            string value = current is not null && current.StateComplete && domain.AuthorityComplete
                ? uiText.Format(Ui1TextKey.SaveStatusRuntimeUnlockCaptured, current.Count, domain.Count)
                : uiText.Get(Ui1TextKey.SaveStatusDomainUnavailable);
            AddKeyValue(_unlockRows, LocalizeDomain(uiText, domain.Domain), value);
        }
    }

    private void RebuildFingerprintMeta(IUiTextProvider uiText, RuntimeAuthorityEnvironmentSnapshot environment)
    {
        RuntimeAuthoritySnapshot authority = environment.Authority;
        string overall = string.IsNullOrWhiteSpace(authority.Fingerprint.OverallSemanticHash)
            ? uiText.Get(Ui1TextKey.SaveStatusHashUnavailable)
            : authority.Fingerprint.OverallSemanticHash;
        string baseline = string.IsNullOrWhiteSpace(environment.Interpretation.ComparisonBaselineGameVersion)
            ? uiText.Get(Ui1TextKey.SaveStatusBaselineNone)
            : environment.Interpretation.ComparisonBaselineGameVersion;
        string candidate = string.IsNullOrWhiteSpace(environment.BaselineCandidatePath)
            ? uiText.Get(Ui1TextKey.SaveStatusHashUnavailable)
            : environment.BaselineCandidatePath;
        _fingerprintMeta.Text = uiText.Format(
            Ui1TextKey.SaveStatusFingerprintMeta,
            overall,
            baseline,
            candidate);
    }

    private string ResolveModelStatus(IUiTextProvider uiText)
    {
        if (_runtime.IsCompatibilityFallback)
            return uiText.Get(Ui1TextKey.SaveStatusModelBestEffort);
        if (_runtime.RequiresCompatibilityWarning || !_runtime.RuntimeAccepted)
            return uiText.Get(Ui1TextKey.SaveStatusModelUnverified);
        return uiText.Get(Ui1TextKey.SaveStatusModelKnown);
    }

    private static string LocalizeDomain(IUiTextProvider uiText, string domain) => domain switch
    {
        RuntimeAuthorityDomains.Characters => uiText.Get(Ui1TextKey.SaveStatusDomainCharacters),
        RuntimeAuthorityDomains.Cards => uiText.Get(Ui1TextKey.SaveStatusDomainCards),
        RuntimeAuthorityDomains.Relics => uiText.Get(Ui1TextKey.SaveStatusDomainRelics),
        RuntimeAuthorityDomains.Ancients => uiText.Get(Ui1TextKey.SaveStatusDomainAncients),
        RuntimeAuthorityDomains.Potions => uiText.Get(Ui1TextKey.SaveStatusDomainPotions),
        RuntimeAuthorityDomains.Events => uiText.Get(Ui1TextKey.SaveStatusDomainEvents),
        RuntimeAuthorityDomains.Bosses => uiText.Get(Ui1TextKey.SaveStatusDomainBosses),
        _ => domain
    };

    private static (VBoxContainer Rows, PanelContainer Panel) CreateSection(VBoxContainer parent)
    {
        var panel = new PanelContainer();
        Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Card, 4f, 1, 14f);
        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 7);
        panel.AddChild(rows);
        parent.AddChild(panel);
        return (rows, panel);
    }

    private static VBoxContainer CreateSectionWithHeader(
        VBoxContainer parent,
        out Label title,
        out Label message)
    {
        var panel = new PanelContainer();
        Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Card, 4f, 1, 14f);

        var section = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        section.AddThemeConstantOverride("separation", 7);
        title = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        message = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        section.AddChild(title);
        section.AddChild(message);
        var separator = new HSeparator();
        Ui1Theme.ApplySeparator(separator);
        section.AddChild(separator);

        // Only this body is rebuilt. Header/message/separator are fixed children so
        // localization refresh cannot QueueFree the controls retained by the page.
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 7);
        section.AddChild(body);
        panel.AddChild(section);
        parent.AddChild(panel);
        return body;
    }

    private static void AddSectionTitle(VBoxContainer rows, string text)
    {
        rows.AddChild(Ui1Theme.Label(text, Ui1TextRole.SectionTitle));
        var separator = new HSeparator();
        Ui1Theme.ApplySeparator(separator);
        rows.AddChild(separator);
    }

    private static void AddKeyValue(VBoxContainer rows, string key, string value, Color? valueColor = null)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 16);
        var keyLabel = Ui1Theme.Label(key, Ui1TextRole.Meta);
        keyLabel.CustomMinimumSize = new Vector2(220f, 0f);
        var valueLabel = Ui1Theme.Label(value, Ui1TextRole.Body, true);
        valueLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (valueColor.HasValue) valueLabel.AddThemeColorOverride("font_color", valueColor.Value);
        row.AddChild(keyLabel);
        row.AddChild(valueLabel);
        rows.AddChild(row);
    }

    private static void ClearRows(VBoxContainer rows)
    {
        foreach (Node child in rows.GetChildren())
        {
            rows.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static string ShortHash(string hash) => string.IsNullOrWhiteSpace(hash)
        ? "—"
        : hash.Length <= 12 ? hash : hash[..12] + "…";
}
