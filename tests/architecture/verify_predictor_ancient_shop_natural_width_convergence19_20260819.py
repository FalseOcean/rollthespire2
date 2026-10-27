from pathlib import Path

root = Path(__file__).resolve().parents[2]
ancient = (root / 'Ui/Components/AnalysisAncientGroupCard.cs').read_text()
shop = (root / 'Ui/Components/AnalysisRelicShopGroupCard.cs').read_text()
allocator = (root / 'Ui/Components/AdaptiveInlineWidthAllocator.cs').read_text()

def req(name, ok):
    print(f'{name}={str(bool(ok)).lower()}')
    if not ok:
        raise AssertionError(name)

req('sharedAdaptiveAllocator', 'internal static class AdaptiveInlineWidthAllocator' in allocator and 'desiredTotal <= usable + 0.5f' in allocator)
req('naturalWidthLeavesRightRemainder', 'return desired;' in allocator and 'leave' in allocator and 'blank on the right' in allocator)
req('shortItemsCompleteFirst', 'Progressive fill / water-filling' in allocator and 'active.RemoveAt' in allocator)
req('ancientUsesSharedAllocator', 'AdaptiveInlineWidthAllocator.Allocate' in ancient and 'OptionMinimumItemWidth = 96f' in ancient)
req('ancientSingleRowLeftAligned', 'Name = "OptionSlots"' in ancient and 'Alignment = BoxContainer.AlignmentMode.Begin' in ancient)
req('ancientNoRigidEqualSlotAlgorithm', 'equalWidth' not in ancient and 'OptionBaselineMinimumWidth' not in ancient and 'donorFloor' not in ancient)
req('ancientFullNameBeforeEllipsis', 'name.ClipText = clipped;' in ancient and 'width + 0.5f < _optionDesiredWidths[index]' in ancient)
req('shopUsesSharedAllocator', 'AdaptiveInlineWidthAllocator.Allocate(usable, desired, minimum)' in shop)
req('shopSingleRowLeftAligned', 'Name = "ShopSequence"' in shop and 'Alignment = BoxContainer.AlignmentMode.Begin' in shop)
req('shopNoRigidEqualShare', 'float equalShare = usable / count;' not in shop)
req('shopMeasuresLocalizedNames', '_measureLabel.GetMinimumSize().X' in shop and 'measuredText[index]' in shop)
req('shopFullNameBeforeEllipsis', '_nameLabels[index].ClipText = clipped;' in shop and 'widths[index] + 0.5f < desired[index]' in shop)
req('shopStillFiveIconNames', 'ShopResultCount = 5' in shop and 'ShopRelicName' in shop and 'ShopRelicHost' in shop)
