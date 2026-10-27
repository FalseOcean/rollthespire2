using Godot;
using RolltheSpire2.Core.Collections;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class SeedLibraryCanvas
{
    private readonly CardCollectionStore? _cardCollections;
    private readonly IGameIconResolver _collectionIcons = new ReflectionGameIconResolver("card-collections");
    private CardCollectionEditorOverlay? _collectionEditor;
    private Button? _newCardCollection, _returnFromCollections;
    private string _selectedCollection = "";
    private CardCollectionSnapshot[] _visibleCollections = [];
    private bool _collectionReturnToSearch;
    internal event Action? CardCollectionReturnRequested;

    private void AttachCardCollectionEditor(Control shell)
    {
        if (_cardCollections is null) return;
        _collectionEditor = new();
        shell.AddChild(_collectionEditor);
        _collectionEditor.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _collectionEditor.Cancelled += EndModal;
        _collectionEditor.SaveRequested += (id, name, cards) =>
        {
            try
            {
                CardCollectionSnapshot saved = _cardCollections.Save(id, name, cards);
                _selectedCollection = saved.Id;
                _collectionEditor.CloseCommitted();
                EndModal();
                ClearFilter();
                RefreshEntries();
                ShowReceipt(T("牌集已保存。已选用的搜索条件保持原快照，可在筛种页更新。",
                    "Collection saved. Existing search conditions keep their snapshot; update it in Search when needed."));
            }
            catch (Exception ex) { _collectionEditor.ShowIssue(Failure(ex)); }
        };
    }

    internal void OpenCardCollection(string? id, bool returnToSearch = false)
    {
        _collectionReturnToSearch = returnToSearch;
        if (id is not null) _selectedCollection = id;
        ShowSection(LibrarySection.CardCollections);
        RefreshEntries();
        if (id is not null)
        {
            _cardCollections!.Refresh();
            if (_cardCollections.TryGet(id, out var collection)) EditCardCollection(collection);
            else ShowIssue(T("原牌集当前不可用；筛种中保存的成员快照仍保留。",
                "The source collection is unavailable; the member snapshot in Search is retained."));
        }
    }

    private void EditCardCollection(CardCollectionSnapshot? source, bool copy = false)
    {
        if (_collectionEditor is null) return;
        BeginModal();
        try { _collectionEditor.Open(_language, source, copy); }
        catch (Exception ex)
        {
            _collectionEditor.CloseCommitted();
            EndModal();
            RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(ex,
                "CardCollectionEditorFailed", nameof(SeedLibraryCanvas), "OpenCardCollection", "issues.library_failed");
            ShowIssue(T("未能打开牌集编辑器。原牌集已保留。", "The editor could not open. The saved collection is preserved.") + "\n" + ex.Message);
        }
    }

    private IEnumerable<bool> PresentCardCollectionList()
    {
        if (_cardCollections is null) { _visibleCollections = []; yield break; }
        _cardCollections.Refresh();
        _visibleCollections = _cardCollections.GetAll().Where(c => Matches(c.Name))
            .OrderByDescending(c => c.UpdatedAtUtc).ThenBy(c => c.Name, StringComparer.CurrentCulture).ToArray();
        if (!_visibleCollections.Any(c => c.Id == _selectedCollection))
            _selectedCollection = _visibleCollections.FirstOrDefault()?.Id ?? "";
        if (_cardCollections.LoadIssues.Count != 0)
            ShowIssue(T("部分牌集无法读取，原文件已保留。", "Some collections could not be read; original files are preserved.")
                + "\n" + string.Join("\n", _cardCollections.LoadIssues));
        foreach (var collection in _visibleCollections)
        {
            AddListItem(collection.Id, collection.Name,
                T($"{collection.Cards.Count} 张牌 · 版本 {collection.Revision}",
                    $"{collection.Cards.Count} cards · revision {collection.Revision}"),
                collection.Id == _selectedCollection,
                () => { _selectedCollection = collection.Id; SelectDetail(); });
            yield return true;
        }
    }

    private IEnumerable<bool> PresentCardCollectionDetail()
    {
        var selected = _visibleCollections.FirstOrDefault(c => c.Id == _selectedCollection);
        if (selected is null)
        {
            EmptyDetail(T("新建一份牌集，连续勾选想保存的卡牌。之后在筛种的战斗奖励中选用。",
                "Create a collection and select cards to save. Choose it later in Search's combat rewards."));
            yield break;
        }
        var actions = DetailHeading(selected.Name,
            T($"{selected.Cards.Count} 张牌 · 不区分升级与附魔", $"{selected.Cards.Count} cards · matches all upgrades and enchantments"));
        AddAction(actions, T("编辑牌集", "Edit collection"), () => EditCardCollection(selected), true).Name = "EditCardCollection";
        AddAction(actions, T("另存副本", "Duplicate"), () => EditCardCollection(selected, copy: true)).Name = "DuplicateCardCollection";
        AddDeleteAction(ManagementActions(), () => ConfirmDeleteCardCollection(selected)).Name = "DeleteCardCollection";
        yield return true;
        foreach (var card in selected.Cards)
        {
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 10);
            var descriptor = _collectionIcons.Resolve(card, GameContentKind.Card, IconVariant.Small);
            if (descriptor.Texture is { } texture)
                row.AddChild(new TextureRect { Texture = texture, CustomMinimumSize = new(24, 24),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore });
            row.AddChild(Text(_names.Resolve(card, GameContentKind.Card), 17));
            _detail.AddChild(row);
            yield return true;
        }
    }

    private void ConfirmDeleteCardCollection(CardCollectionSnapshot collection)
    {
        if (_confirmation is null || _cardCollections is null) return;
        _confirmed = () =>
        {
            if (!_cardCollections.Delete(collection.Id))
                throw new IOException(T("牌集当前不可用，请刷新后重试。", "The collection is unavailable; refresh and retry."));
            RefreshEntries();
            ShowReceipt(T("牌集已删除；预设和历史中的成员快照仍保留。",
                "Collection deleted; member snapshots in presets and history are retained."));
        };
        BeginModal();
        _confirmation.Open(T("删除牌集", "Delete collection"),
            T($"删除「{collection.Name}」？已保存的搜索条件不会被删除。",
                $"Delete “{collection.Name}”? Saved search conditions will be retained."),
            T("删除", "Delete"), T("取消", "Cancel"));
    }
}
