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
/// Current game/save/environment inspector with an explicit compute-device probe. It consumes the RT2-owned
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
        Theme = MainPanelTheme.Create();
        foreach (string side in new[] { "left", "top", "right", "bottom" })
            AddThemeConstantOverride("margin_" + side, 16);
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var root = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 12);
        _pageTitle = WorkspacePalette.MainPanel.Label(string.Empty, 24, heading: true);
        _subtitle = WorkspacePalette.MainPanel.Label(string.Empty, 16, secondary: true);
        _subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_pageTitle);
        root.AddChild(_subtitle);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 16);

        (_deviceRows, _) = CreateSection(content);
        (_gameRows, _) = CreateSection(content);
        _semanticRows = CreateSectionWithHeader(content, out _semanticSummary, out _semanticMessage);
        _unlockRows = CreateSectionWithHeader(content, out _unlockSummary, out _unlockMessage);

        var fingerprintPanel = CreateStatusPanel();
        _fingerprintMeta = MainPanelTheme.Label(string.Empty, Ui1TextRole.Code, true);
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
            var section = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            section.AddThemeConstantOverride("separation", 12);
            var header = WorkspacePalette.MainPanel.CompactButton(string.Empty, 36, 18);
            header.ToggleMode = true;
            header.ButtonPressed = true;
            header.Alignment = HorizontalAlignment.Left;
            header.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            // Expansion is disclosure, so keep the title on its content plane.
            WorkspacePalette.MainPanel.SetModeActive(header, false, horizontalPadding: 0);
            var hover = WorkspacePalette.MainPanel.Box(WorkspacePalette.MainPanel.Hover);
            hover.ContentMarginLeft = hover.ContentMarginRight = 0;
            hover.ContentMarginTop = hover.ContentMarginBottom = 2;
            header.AddThemeStyleboxOverride("hover_pressed", hover);
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
    internal Func<bool> SearchBusy { get; set; } = () => false;

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
        AddKeyValue(_deviceRows, "CPU", identity.CpuIdentity);
        AddKeyValue(_deviceRows, text.Get("ui1.devices.cpu_usage"), text.Get("ui1.devices.cpu_available"));
        var cpu = CpuCostCalibration.DisplaySummary();
        if (cpu.Length == 0)
            AddKeyValue(_deviceRows, text.Get("ui1.devices.relative"), text.Get("ui1.devices.reference_default"));
        foreach (var row in cpu)
            AddKeyValue(_deviceRows, text.Format("ui1.devices.cpu_workers", row.Workers),
                text.Format("ui1.devices.factor", 1 / row.Ratio, row.Samples));
        AddKeyValue(_deviceRows, "GPU", identity.HasKnownGpu ? identity.GpuIdentity : text.Get("ui1.devices.unidentified"));
        _deviceRows.AddChild(new RolltheSpire2.Ui.Components.GpuInitializationPanel(text.LanguageCode, () => SearchBusy()));
        var gpu = GpuCostCalibration.DisplaySummary();
        AddKeyValue(_deviceRows, text.Get("ui1.devices.relative"), gpu.Comparable > 0
            ? text.Format("ui1.devices.factor", 1 / gpu.Ratio, gpu.Comparable)
            : text.Get("ui1.devices.reference_default"));
        AddKeyValue(_deviceRows, text.Get("ui1.devices.recorded"), gpu.Recorded.ToString());
        AddKeyValue(_deviceRows, text.Get("ui1.devices.backend"), string.IsNullOrEmpty(identity.RenderingBackend) ? "—" : identity.RenderingBackend);
        var help = WorkspacePalette.MainPanel.Label(text.Get("ui1.devices.help"), 15, secondary: true);
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
                (uiText.Format(Ui1TextKey.SaveStatusSemanticVerified, interpretation.MatchedBaselineGameVersion), MainPanelTheme.Palette.Success),
            SemanticEnvironmentStatus.KnownBaselineMatchButGameVersionUnverified =>
                (uiText.Format(Ui1TextKey.SaveStatusSemanticCrossVersionMatch, interpretation.MatchedBaselineGameVersion), MainPanelTheme.Palette.Partial),
            SemanticEnvironmentStatus.SemanticMismatch when interpretation.SameGameVersionBaselineKnown =>
                (uiText.Format(Ui1TextKey.SaveStatusSemanticMismatchSameVersion, interpretation.ComparisonBaselineGameVersion), MainPanelTheme.Palette.Warning),
            SemanticEnvironmentStatus.SemanticMismatch =>
                (uiText.Get(Ui1TextKey.SaveStatusSemanticMismatchUnknownVersion), MainPanelTheme.Palette.Warning),
            _ when !authority.Fingerprint.Complete =>
                (uiText.Get(Ui1TextKey.SaveStatusSemanticFingerprintIncomplete), MainPanelTheme.Palette.Unknown),
            _ =>
                (uiText.Get(Ui1TextKey.SaveStatusSemanticAwaitingBaseline), MainPanelTheme.Palette.Unknown)
        };
        _semanticSummary.AddThemeColorOverride("font_color", color);
        _semanticMessage.AddThemeColorOverride("font_color", color);

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
                    ? MainPanelTheme.Palette.Warning
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
            VanillaUnlockOverallStatus.Full => (uiText.Get(Ui1TextKey.SaveStatusUnlockFull), MainPanelTheme.Palette.Success),
            VanillaUnlockOverallStatus.Partial => (uiText.Get(Ui1TextKey.SaveStatusUnlockPartial), MainPanelTheme.Palette.Partial),
            _ => (uiText.Get(Ui1TextKey.SaveStatusUnlockUnknown), MainPanelTheme.Palette.Unknown)
        };
        _unlockSummary.AddThemeColorOverride("font_color", color);
        _unlockMessage.AddThemeColorOverride("font_color", color);

        if (interpretation.VanillaUnlockCoverage.Count > 0)
        {
            foreach (VanillaUnlockDomainCoverage coverage in interpretation.VanillaUnlockCoverage)
            {
                string value = coverage.Complete
                    ? uiText.Format(Ui1TextKey.SaveStatusUnlockCoverage, coverage.Unlocked, coverage.VanillaUniverse)
                    : uiText.Get(Ui1TextKey.SaveStatusDomainUnavailable);
                AddKeyValue(_unlockRows, LocalizeDomain(uiText, coverage.Domain), value,
                    coverage.Complete && !coverage.IsFull ? MainPanelTheme.Palette.Partial : null);
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

    private static PanelContainer CreateStatusPanel()
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var box = WorkspacePalette.MainPanel.ContentBox(borderWidth: 0);
        box.ContentMarginLeft = box.ContentMarginRight = 16;
        box.ContentMarginTop = box.ContentMarginBottom = 12;
        panel.AddThemeStyleboxOverride("panel", box);
        return panel;
    }

    private static (VBoxContainer Rows, PanelContainer Panel) CreateSection(VBoxContainer parent)
    {
        var panel = CreateStatusPanel();
        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 8);
        panel.AddChild(rows);
        parent.AddChild(panel);
        return (rows, panel);
    }

    private static VBoxContainer CreateSectionWithHeader(
        VBoxContainer parent,
        out Label title,
        out Label message)
    {
        var panel = CreateStatusPanel();

        var section = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        section.AddThemeConstantOverride("separation", 12);
        title = WorkspacePalette.MainPanel.Label(string.Empty, 18, heading: true);
        message = WorkspacePalette.MainPanel.Label(string.Empty, 16);
        message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        section.AddChild(title);
        section.AddChild(message);
        var separator = new HSeparator();
        MainPanelTheme.ApplySeparator(separator);
        section.AddChild(separator);

        // Only this body is rebuilt. Header/message/separator are fixed children so
        // localization refresh cannot QueueFree the controls retained by the page.
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8);
        section.AddChild(body);
        panel.AddChild(section);
        parent.AddChild(panel);
        return body;
    }

    private static void AddSectionTitle(VBoxContainer rows, string text)
    {
        rows.AddChild(WorkspacePalette.MainPanel.Label(text, 18, heading: true));
        var separator = new HSeparator();
        MainPanelTheme.ApplySeparator(separator);
        rows.AddChild(separator);
    }

    private static void AddKeyValue(VBoxContainer rows, string key, string value, Color? valueColor = null)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 16);
        var keyLabel = WorkspacePalette.MainPanel.Label(key, 16, secondary: true);
        keyLabel.VerticalAlignment = VerticalAlignment.Top;
        keyLabel.CustomMinimumSize = new Vector2(220f, 0f);
        var valueLabel = WorkspacePalette.MainPanel.Label(value, 16);
        valueLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        valueLabel.VerticalAlignment = VerticalAlignment.Top;
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
