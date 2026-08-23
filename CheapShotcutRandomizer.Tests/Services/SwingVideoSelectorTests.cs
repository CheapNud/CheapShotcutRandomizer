using CheapShotcutRandomizer.Core.Models;
using CheapShotcutRandomizer.Services;
using FluentAssertions;

namespace CheapShotcutRandomizer.Tests.Services;

/// <summary>
/// Tests for the swing selector: long clips anchor the sequence in random order,
/// short clips are distributed evenly into the gaps between them
/// </summary>
public class SwingVideoSelectorTests
{
    private static Entry MakeEntry(string producer, int seconds) => new()
    {
        Producer = producer,
        In = "00:00:00.000",
        Out = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.fff")
    };

    /// <summary>
    /// Lengths of the consecutive short-clip runs around the long clips: first
    /// element = shorts before the first long, last = shorts after the last long,
    /// inner elements = the gaps between consecutive longs.
    /// </summary>
    private static List<int> ShortRunLengths(List<Entry> selected, int longDuration)
    {
        var runs = new List<int>();
        var runLength = 0;
        foreach (var entry in selected)
        {
            if (entry.Duration == longDuration)
            {
                runs.Add(runLength);
                runLength = 0;
            }
            else
            {
                runLength++;
            }
        }
        runs.Add(runLength);
        return runs;
    }

    [Fact]
    public void Long_Clips_Anchor_And_Shorts_Distribute_Equally()
    {
        // 4 long (60s) and 3 short (5s) clips: median split puts exactly the three
        // 5s clips in the short half. 3 gaps between 4 longs, one short per gap.
        var pool = Enumerable.Range(0, 4).Select(i => MakeEntry($"long{i}", 60))
            .Concat(Enumerable.Range(0, 3).Select(i => MakeEntry($"short{i}", 5)))
            .ToList();

        var selected = SwingVideoSelector.Select(pool, targetDuration: 600);

        selected.Should().HaveCount(7, "the budget fits every clip");

        var runs = ShortRunLengths(selected, longDuration: 60);
        runs.First().Should().Be(0, "the sequence starts with a long clip");
        runs.Last().Should().Be(0, "the sequence ends with a long clip");
        runs.Skip(1).SkipLast(1).Should().HaveCount(3).And.OnlyContain(r => r == 1,
            "3 shorts distribute equally across the 3 gaps between longs");
    }

    [Fact]
    public void Remainder_Shorts_Add_At_Most_One_Extra_Per_Gap()
    {
        // 3 longs give 2 gaps; 3 shorts deal out as 1 and 2 in some order
        var pool = Enumerable.Range(0, 3).Select(i => MakeEntry($"long{i}", 60))
            .Concat(Enumerable.Range(0, 3).Select(i => MakeEntry($"short{i}", 5)))
            .ToList();

        var selected = SwingVideoSelector.Select(pool, targetDuration: 600);

        selected.Should().HaveCount(6);

        var runs = ShortRunLengths(selected, longDuration: 60);
        runs.First().Should().Be(0);
        runs.Last().Should().Be(0);
        runs.Skip(1).SkipLast(1).OrderBy(r => r).Should().Equal(1, 2);
    }

    [Fact]
    public void Fewer_Than_Two_Long_Picks_Returns_Everything_Selected()
    {
        // Two clips: the median split yields one "short" and one "long" - no gaps
        // exist, the selection just comes back complete
        var pool = new List<Entry> { MakeEntry("a", 5), MakeEntry("b", 8) };

        var selected = SwingVideoSelector.Select(pool, targetDuration: 600);

        selected.Should().HaveCount(2);
    }

    [Fact]
    public void Respects_Target_Duration_Budget()
    {
        var pool = Enumerable.Range(0, 20).Select(i => MakeEntry($"clip{i}", 30)).ToList();

        var selected = SwingVideoSelector.Select(pool, targetDuration: 100);

        selected.Sum(e => e.Duration).Should().BeLessThanOrEqualTo(100);
        selected.Should().NotBeEmpty();
    }

    [Fact]
    public void Never_Picks_The_Same_Entry_Twice()
    {
        var pool = Enumerable.Range(0, 6).Select(i => MakeEntry($"clip{i}", 10)).ToList();

        var selected = SwingVideoSelector.Select(pool, targetDuration: 600);

        selected.Should().OnlyHaveUniqueItems();
        selected.Should().HaveCount(6);
    }

    [Fact]
    public void Falls_Back_To_The_Other_Half_When_One_Runs_Dry()
    {
        // One short clip, three long ones: after the short half empties,
        // selection continues from the long half instead of stopping
        var pool = new List<Entry>
        {
            MakeEntry("short0", 5),
            MakeEntry("long0", 60),
            MakeEntry("long1", 60),
            MakeEntry("long2", 60)
        };

        var selected = SwingVideoSelector.Select(pool, targetDuration: 600);

        selected.Should().HaveCount(4);
    }

    [Fact]
    public void Empty_And_Zero_Duration_Pools_Return_Empty()
    {
        SwingVideoSelector.Select([], 100).Should().BeEmpty();
        SwingVideoSelector.Select([MakeEntry("broken", 0)], 100).Should().BeEmpty();
    }
}
