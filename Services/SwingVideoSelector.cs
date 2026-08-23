using CheapShotcutRandomizer.Core.Models;
using CheapHelpers.Extensions;

namespace CheapShotcutRandomizer.Services;

/// <summary>
/// Selection mode that swings between short and long clips: the pool is split at
/// the median duration and picks alternate randomly between the two halves until
/// the target duration is filled. The result is arranged as randomly ordered long
/// clips with the short clips distributed evenly into the gaps between them.
/// The arranged order IS the playlist order.
/// </summary>
public static class SwingVideoSelector
{
    public static List<Entry> Select(IEnumerable<Entry> pool, int targetDuration)
    {
        var sorted = pool.Where(e => e.Duration > 0).OrderBy(e => e.Duration).ToList();
        if (sorted.Count == 0)
            return [];

        // Long half absorbs the middle element on odd counts
        var half = sorted.Count / 2;
        var shortHalf = sorted.Take(half).ToList();
        var longHalf = sorted.Skip(half).ToList();

        var pickedShorts = new List<Entry>();
        var pickedLongs = new List<Entry>();
        var remaining = targetDuration;
        var pickShort = true;

        while (true)
        {
            var tookShort = pickShort;
            var picked = TakeRandomFitting(pickShort ? shortHalf : longHalf, remaining);
            if (picked == null)
            {
                tookShort = !pickShort;
                picked = TakeRandomFitting(pickShort ? longHalf : shortHalf, remaining);
            }

            if (picked == null)
                break;

            (tookShort ? pickedShorts : pickedLongs).Add(picked);
            remaining -= picked.Duration;
            pickShort = !pickShort;
        }

        return Arrange(pickedShorts, pickedLongs);
    }

    private static Entry? TakeRandomFitting(List<Entry> half, int remaining)
    {
        var fitting = half.Where(e => e.Duration <= remaining).ToList();
        if (fitting.Count == 0)
            return null;

        var picked = fitting[Random.Shared.Next(fitting.Count)];
        half.Remove(picked);
        return picked;
    }

    /// <summary>
    /// Long clips anchor the sequence in random order; short clips are dealt as
    /// evenly as possible into the gaps strictly between consecutive long clips,
    /// remainder going to randomly chosen gaps. Fewer than two long clips means
    /// no gaps exist, so the whole selection is simply shuffled.
    /// </summary>
    private static List<Entry> Arrange(List<Entry> shorts, List<Entry> longs)
    {
        if (longs.Count < 2)
        {
            List<Entry> everything = [.. shorts, .. longs];
            return [.. everything.Shuffle()];
        }

        var shuffledLongs = longs.Shuffle().ToList();
        var shuffledShorts = shorts.Shuffle().ToList();

        var gaps = shuffledLongs.Count - 1;
        var perGap = shuffledShorts.Count / gaps;
        var remainder = shuffledShorts.Count % gaps;

        // Gaps carrying one extra short (remainder < gaps, so this terminates)
        var extraGaps = new HashSet<int>();
        while (extraGaps.Count < remainder)
        {
            extraGaps.Add(Random.Shared.Next(gaps));
        }

        var result = new List<Entry>();
        var shortCursor = 0;

        for (int i = 0; i < shuffledLongs.Count; i++)
        {
            result.Add(shuffledLongs[i]);

            if (i >= gaps)
                continue;

            var gapSize = perGap + (extraGaps.Contains(i) ? 1 : 0);
            for (int s = 0; s < gapSize; s++)
            {
                result.Add(shuffledShorts[shortCursor++]);
            }
        }

        return result;
    }
}
