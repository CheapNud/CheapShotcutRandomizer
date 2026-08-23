using CheapShotcutRandomizer.Core.Models;
using CheapShotcutRandomizer.Services;
using FluentAssertions;

namespace CheapShotcutRandomizer.Tests.Services;

/// <summary>
/// Tests for selection-pool deduplication by resource file + clip in/out range
/// </summary>
public class ShotcutServiceDedupeTests
{
    private static Mlt BuildProjectWithChains(params (string ChainId, string Resource)[] chains)
    {
        return new Mlt
        {
            Chain = [.. chains.Select(c => new Chain
            {
                Id = c.ChainId,
                Property = [new() { Name = "resource", Text = c.Resource }]
            })]
        };
    }

    private static Entry MakeEntry(string producer, string inTime = "00:00:00.000", string outTime = "00:00:10.000") =>
        new() { Producer = producer, In = inTime, Out = outTime };

    [Fact]
    public void Same_Resource_Same_Range_Collapses_Even_Across_Different_Producers()
    {
        // Two chains pointing at the same file (as happens when the same clip
        // exists in two merged projects)
        var project = BuildProjectWithChains(
            ("chainA", @"D:\media\clip.mp4"),
            ("chainB", @"d:\media\CLIP.mp4"));

        var deduped = ShotcutService.DedupeEntries(project,
            [MakeEntry("chainA"), MakeEntry("chainB")]);

        deduped.Should().HaveCount(1);
    }

    [Fact]
    public void Same_Resource_Different_Range_Survives()
    {
        var project = BuildProjectWithChains(("chainA", @"D:\media\clip.mp4"));

        var deduped = ShotcutService.DedupeEntries(project,
        [
            MakeEntry("chainA", "00:00:00.000", "00:00:10.000"),
            MakeEntry("chainA", "00:00:10.000", "00:00:20.000")
        ]);

        deduped.Should().HaveCount(2);
    }

    [Fact]
    public void Duplicate_Entries_Within_One_Playlist_Collapse()
    {
        var project = BuildProjectWithChains(("chainA", @"D:\media\clip.mp4"));

        var deduped = ShotcutService.DedupeEntries(project,
            [MakeEntry("chainA"), MakeEntry("chainA"), MakeEntry("chainA")]);

        deduped.Should().HaveCount(1);
    }

    [Fact]
    public void Producers_Without_A_Chain_Fall_Back_To_Producer_Id()
    {
        var project = BuildProjectWithChains(("chainA", @"D:\media\clip.mp4"));

        var deduped = ShotcutService.DedupeEntries(project,
            [MakeEntry("orphan1"), MakeEntry("orphan2"), MakeEntry("orphan1")]);

        deduped.Should().HaveCount(2, "distinct unresolvable producers must not collapse into each other");
    }
}
