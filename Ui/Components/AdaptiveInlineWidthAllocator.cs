using System;
using System.Collections.Generic;
using System.Linq;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Presentation-only allocator for short, single-row identity lists.
/// Natural item widths win whenever they fit. If the row is genuinely too narrow,
/// each item keeps a small floor and the remaining width is progressively shared;
/// short items reach their full width first and donate the rest to longer neighbors.
/// </summary>
internal static class AdaptiveInlineWidthAllocator
{
    public static float[] Allocate(
        float availableWidth,
        IReadOnlyList<float> desiredWidths,
        IReadOnlyList<float> minimumWidths)
    {
        ArgumentNullException.ThrowIfNull(desiredWidths);
        ArgumentNullException.ThrowIfNull(minimumWidths);
        if (desiredWidths.Count != minimumWidths.Count)
        {
            throw new ArgumentException("Desired and minimum width lists must have the same length.");
        }

        int count = desiredWidths.Count;
        if (count == 0)
        {
            return Array.Empty<float>();
        }

        var desired = new float[count];
        var minimum = new float[count];
        for (int index = 0; index < count; index++)
        {
            desired[index] = Math.Max(1f, desiredWidths[index]);
            minimum[index] = Math.Min(desired[index], Math.Max(1f, minimumWidths[index]));
        }

        float usable = Math.Max(1f, availableWidth);
        float desiredTotal = desired.Sum();
        if (desiredTotal <= usable + 0.5f)
        {
            // Full names fit. Keep every item at its natural content width and leave
            // any remaining room blank on the right instead of fabricating rigid slots.
            return desired;
        }

        float minimumTotal = minimum.Sum();
        if (minimumTotal >= usable - 0.5f)
        {
            // Extreme fallback: even all declared floors do not fit. Scale those floors
            // proportionally so the HBox itself still remains inside its single-row budget.
            float scale = usable / Math.Max(1f, minimumTotal);
            return minimum.Select(width => Math.Max(1f, width * scale)).ToArray();
        }

        float[] widths = minimum.ToArray();
        float remaining = usable - minimumTotal;
        var active = Enumerable.Range(0, count)
            .Where(index => desired[index] - widths[index] > 0.5f)
            .ToList();

        // Progressive fill / water-filling: all still-truncated items receive the same
        // incremental budget. Short items therefore become complete first; their unused
        // budget then flows to longer neighbors before ellipsis is required.
        while (remaining > 0.5f && active.Count > 0)
        {
            float share = remaining / active.Count;
            bool saturatedAny = false;

            for (int activeIndex = active.Count - 1; activeIndex >= 0; activeIndex--)
            {
                int index = active[activeIndex];
                float need = desired[index] - widths[index];
                if (need <= share + 0.5f)
                {
                    widths[index] += need;
                    remaining -= need;
                    active.RemoveAt(activeIndex);
                    saturatedAny = true;
                }
            }

            if (remaining <= 0.5f || active.Count == 0)
            {
                break;
            }

            if (!saturatedAny)
            {
                float evenShare = remaining / active.Count;
                foreach (int index in active)
                {
                    widths[index] += evenShare;
                }
                remaining = 0f;
            }
        }

        return widths;
    }
}
